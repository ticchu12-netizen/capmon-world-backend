/**
 * Capmon World — Phase 1 Cloud Functions (v2)
 *
 * P0 fixes applied vs previous-Claude's version:
 *   - §5.1 FIX: Transaction reads now precede all writes (was broken)
 *   - §5.2 FIX: Cosmetic rank-up via totalWon thresholds added
 *   - §5.3 FIX: reviveStarter Cloud Function ported forward from baseline
 *   - CORS FIX: Explicitly allow capmon.fun + Netlify origins on callable funcs
 *
 * Hackathon additions (May 7+):
 *   - linkWallet: verify Phantom ownership + mirror staked NFT tier into Firestore
 *   - resolveMatch: applies tier multiplier on wins (1.0×/1.4×/1.9×/2.8×) using
 *     the stakedTier mirrored by linkWallet. AI pool draws scale by multiplier
 *     so bounded-pool invariant from handoff §2.4 stays intact.
 *   - getCapbotData: read-only endpoint for the Unity Capbot tab. Returns
 *     stake state + recent autonomous-battle activity + recent on-chain
 *     Pattern 2 brain-upgrade tx signatures.
 *
 * Architecture (from handoff §2):
 *   - Firestore is single source of truth for Cap Coins
 *   - Clients CANNOT write coin fields directly (blocked by rules)
 *   - All balance mutations go through these Cloud Functions
 *   - resolveMatch validates bet, applies AI pool logic, uses transactions
 *     for atomic writes, deduplicates via idempotency keys, tracks anti-cheat
 *   - reviveStarter handles dead-starter revival via cross-starter coin transfer
 *   - linkWallet verifies Phantom signature, queries Solana for stake_records,
 *     mirrors highest-tier stake into players/<uid> via admin SDK
 *
 * Deployment:
 *   firebase deploy --only functions
 *   (firestore:rules deploys later, in Section 3)
 */

const { onCall, HttpsError } = require("firebase-functions/v2/https");
const { onRequest } = require('firebase-functions/v2/https');
const { onSchedule } = require("firebase-functions/v2/scheduler");
const { setGlobalOptions } = require("firebase-functions/v2");
const { initializeApp } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

// Solana / signature verification deps (added for linkWallet)
const { Connection, PublicKey } = require("@solana/web3.js");
const nacl = require("tweetnacl");

initializeApp();
const db = getFirestore();

setGlobalOptions({
    region: "us-central1",
    maxInstances: 10,
});

// ============================================================
// CONSTANTS — tweak to tune economy / anti-cheat / rank thresholds
// ============================================================
const CONFIG = {
    // Bet bounds (must match BettingScreen.cs slider)
    MIN_BET: 1000,
    MAX_BET: 10000,

    // AI pool economics (per-player; see handoff §2.4 and §5.13)
    AI_POOL_STARTING: 10_000_000,
    AI_POOL_REFILL_PER_DAY: 1_000_000,
    AI_POOL_MAX_CAP: 50_000_000,

    // Anti-cheat (handoff §2.2 and §5.7)
    CHEAT_MIN_BATTLES_24H: 30,
    CHEAT_WIN_RATE_THRESHOLD: 0.85,

    // Idempotency
    IDEMPOTENCY_TTL_HOURS: 24,

    // Cosmetic rank thresholds (Option A defaults, handoff §5.2)
    // rank is computed from totalWon. Tunable later without data migration.
    RANK_THRESHOLDS: {
        SILVER: 165_000,
        GOLD: 825_000,
        PLATINUM: 3_300_000,
        DIAMOND: 11_000_000,
    },

    // Revive bounds
    REVIVE_MIN_AMOUNT: 10_000,
    REVIVE_MAX_AMOUNT: 50_000,

    // linkWallet — anti-replay window for Phantom signed messages
    LINK_WALLET_MAX_MESSAGE_AGE_MS: 5 * 60 * 1000, // 5 min

    // Tier payout multipliers — index is stakedTier (0..3).
    // MUST mirror Unity-side TIER_MULTIPLIERS in WalletScreen.cs and
    // StarterSelectionScreen.cs. Locked Day 2.
    TIER_MULTIPLIERS: [1.0, 1.4, 1.9, 2.8],
};

// Five rank names. Index = rank value. Bronze=0, Silver=1, ..., Diamond=4.
const RANKS = ["Bronze", "Silver", "Gold", "Platinum", "Diamond"];

// Allowed CORS origins for callable functions. Add new origins here as deploy
// targets change. Regex matches Netlify deploy preview URLs (capmon-world--xxx.netlify.app).
const ALLOWED_ORIGINS = [
    "https://capmon.fun",
    "https://www.capmon.fun",
    "https://capmon-world.netlify.app",
    /^https:\/\/capmon-world--.*\.netlify\.app$/,
];

// ============================================================
// SOLANA CONSTANTS — for linkWallet on-chain reads
// ============================================================
// Hardcoded for hackathon. Rotate post-hackathon and move to functions:secrets.
const HELIUS_RPC = "https://devnet.helius-rpc.com/?api-key=cfd79774-43dd-4cf4-a2dd-aefefe55e6f1";
const STAKING_PROGRAM_ID = new PublicKey("FSenbAEVTgTdfM2723xkk8A2Y5oD8wtmB2EhiWXzpqSg");

// StakeRecord byte layout (verified against state.rs May 7 — 86 bytes total):
//   [ 0.. 8] account discriminator (Anchor 8-byte SHA256 prefix)
//   [ 8..40] owner pubkey         (32)
//   [40..72] nft_asset_id pubkey  (32)
//   [    72] tier                 (u8)
//   [73..77] brain_steps          (u32 LE)
//   [77..85] staked_at            (i64 LE)
//   [    85] bump                 (u8)
const STAKE_RECORD_SIZE = 86;
const STAKE_OWNER_OFFSET = 8;
const STAKE_ASSET_OFFSET = 40;
const STAKE_TIER_OFFSET = 72;
const STAKE_BRAIN_STEPS_OFFSET = 73;

/**
 * Compute rank deterministically from totalWon.
 * Returns integer 0..4.
 */
function computeRankFromTotalWon(totalWon) {
    const t = CONFIG.RANK_THRESHOLDS;
    if (totalWon >= t.DIAMOND) return 4;
    if (totalWon >= t.PLATINUM) return 3;
    if (totalWon >= t.GOLD) return 2;
    if (totalWon >= t.SILVER) return 1;
    return 0;
}

/**
 * Read tier multiplier for a player document. Non-NFT users (stakedTier
 * undefined or -1) fall through to 1.0× — preserves Phase 1 behavior.
 */
function getTierMultiplier(player) {
    const tier = player.stakedTier ?? -1;
    if (tier < 0 || tier >= CONFIG.TIER_MULTIPLIERS.length) return 1.0;
    return CONFIG.TIER_MULTIPLIERS[tier];
}

// ============================================================
// resolveMatch — main battle resolution Cloud Function
// ============================================================
exports.resolveMatch = onCall({
    enforceAppCheck: false,  // §5.6: flip to true after App Check is wired in index.html
    cors: ALLOWED_ORIGINS,
}, async (request) => {

    // ---------- 1. AUTH ----------
    if (!request.auth) {
        throw new HttpsError("unauthenticated", "Must be logged in to resolve a match.");
    }
    const uid = request.auth.uid;

    // ---------- 2. INPUT VALIDATION ----------
    const data = request.data || {};
    const { idempotencyKey, betAmount, playerWon, usedStarter, defeatedAi } = data;

    if (typeof idempotencyKey !== "string" || idempotencyKey.length < 8 || idempotencyKey.length > 64) {
        throw new HttpsError("invalid-argument", "idempotencyKey must be a string 8-64 chars.");
    }
    if (!Number.isInteger(betAmount) || betAmount < CONFIG.MIN_BET || betAmount > CONFIG.MAX_BET) {
        throw new HttpsError("invalid-argument", `betAmount must be an integer between ${CONFIG.MIN_BET} and ${CONFIG.MAX_BET}.`);
    }
    if (typeof playerWon !== "boolean") {
        throw new HttpsError("invalid-argument", "playerWon must be a boolean.");
    }
    const validStarters = ["Rageblaze", "Tsunami", "Healspike"];
    if (!validStarters.includes(usedStarter)) {
        throw new HttpsError("invalid-argument", "usedStarter must be one of Rageblaze, Tsunami, Healspike.");
    }
    if (!validStarters.includes(defeatedAi)) {
        throw new HttpsError("invalid-argument", "defeatedAi must be one of Rageblaze, Tsunami, Healspike.");
    }

    // ---------- 3. IDEMPOTENCY CHECK (outside transaction) ----------
    const idempRef = db.collection("battle_idempotency_keys").doc(`${uid}_${idempotencyKey}`);
    const idempSnap = await idempRef.get();
    if (idempSnap.exists) {
        console.log(`[resolveMatch] Idempotency hit for ${uid}/${idempotencyKey}`);
        return idempSnap.data().result;
    }

    // ---------- 4. TRANSACTION: reads first, then math, then writes ----------
    const playerRef = db.collection("players").doc(uid);
    const statsRef = db.collection("battle_stats").doc(uid);

    const result = await db.runTransaction(async (tx) => {
        // ----- 4a. ALL READS FIRST (§5.1 FIX — was broken before) -----
        const playerSnap = await tx.get(playerRef);
        const statsSnap = await tx.get(statsRef);

        if (!playerSnap.exists) {
            throw new HttpsError("not-found", "Player document does not exist.");
        }
        const player = playerSnap.data();

        // ----- 4b. Bet validation -----
        const starterField = `${usedStarter.toLowerCase()}Coins`;
        const currentStarterBalance = player[starterField] ?? 0;
        if (currentStarterBalance < betAmount) {
            throw new HttpsError("failed-precondition", "Insufficient coins to place this bet.");
        }

        // ----- 4c. Lazy AI pool refill -----
        const aiPoolField = `ai${defeatedAi}Coins`;
        const lastRefillField = `ai${defeatedAi}LastRefill`;
        let currentAiPool = player[aiPoolField] ?? CONFIG.AI_POOL_STARTING;
        const lastRefillMs = player[lastRefillField] ?? Date.now();
        const hoursSinceRefill = (Date.now() - lastRefillMs) / (1000 * 60 * 60);
        if (hoursSinceRefill > 0) {
            const refillAmount = Math.floor((CONFIG.AI_POOL_REFILL_PER_DAY * hoursSinceRefill) / 24);
            currentAiPool = Math.min(currentAiPool + refillAmount, CONFIG.AI_POOL_MAX_CAP);
        }

        // ----- 4d. Compute tier multiplier (NEW — applies on wins only) -----
        // Multiplier is read from Firestore-mirrored stake state (written by
        // linkWallet). Non-NFT users have stakedTier=-1, fall through to 1.0×.
        // Loss payouts are NOT multiplied — would be brutal.
        const multiplier = getTierMultiplier(player);
        const winnings = Math.floor(betAmount * multiplier);

        // ----- 4e. AI pool sufficiency on player win (uses multiplied winnings) -----
        if (playerWon && currentAiPool < winnings) {
            throw new HttpsError("resource-exhausted", `${defeatedAi} AI pool is empty. Try a different AI type.`);
        }

        // ----- 4f. Compute deltas -----
        // AI pool draw is also scaled by multiplier — preserves bounded-pool
        // invariant from handoff §2.4 (King users drain pools 2.8× faster).
        const playerDelta = playerWon ? winnings : -betAmount;
        const aiDelta     = playerWon ? -winnings : betAmount;

        const newStarterBalance = currentStarterBalance + playerDelta;
        const newAiPool = Math.min(Math.max(currentAiPool + aiDelta, 0), CONFIG.AI_POOL_MAX_CAP);

        // ----- 4g. Update totalWon (only on win, multiplied) and recompute rank (§5.2) -----
        const newTotalWon = playerWon ? (player.totalWon ?? 0) + winnings : (player.totalWon ?? 0);
        const newRank = computeRankFromTotalWon(newTotalWon);

        // ----- 4h. Update battle stats for anti-cheat (§2.2) -----
        const now = Date.now();
        const cutoff24h = now - (24 * 60 * 60 * 1000);
        let recentBattles = (statsSnap.exists && statsSnap.data().recentBattles) || [];
        recentBattles = recentBattles.filter(b => b.ts > cutoff24h);
        recentBattles.push({ ts: now, won: playerWon, bet: betAmount });
        if (recentBattles.length > 200) {
            recentBattles = recentBattles.slice(-200);
        }
        const totalBattles = (statsSnap.exists ? (statsSnap.data().totalBattles ?? 0) : 0) + 1;

        // ----- 4i. ALL WRITES (after all reads + math complete) -----
        const updates = {
            [starterField]: newStarterBalance,
            [aiPoolField]: newAiPool,
            [lastRefillField]: now,
            totalWon: newTotalWon,
            rank: newRank,
            lastUpdated: now,
        };
        tx.update(playerRef, updates);

        tx.set(statsRef, {
            uid,
            recentBattles,
            totalBattles,
            lastBattleAt: now,
        }, { merge: true });

        const cachedResult = {
            success: true,
            playerWon,
            playerDelta,
            newStarterBalance,
            newAiPool,
            newTotalWon,
            newRank,
            usedStarter,
            defeatedAi,
            appliedMultiplier: multiplier,   // NEW — Unity displays "+X (M× Tier)"
        };
        tx.set(idempRef, {
            uid,
            result: cachedResult,
            createdAt: now,
            expiresAt: now + (CONFIG.IDEMPOTENCY_TTL_HOURS * 60 * 60 * 1000),
        });

        return { cachedResult, recentBattles };
    });

    // ---------- 5. ANTI-CHEAT EVAL (outside transaction, async-safe) ----------
    try {
        const recent = result.recentBattles;
        if (recent.length >= CONFIG.CHEAT_MIN_BATTLES_24H) {
            const wins = recent.filter(b => b.won).length;
            const winRate = wins / recent.length;
            // Note: §5.7 P1 — change > to >= when next touching this file
            if (winRate > CONFIG.CHEAT_WIN_RATE_THRESHOLD) {
                console.warn(`[resolveMatch] CHEAT FLAG triggered for ${uid}: ${wins}/${recent.length} = ${(winRate*100).toFixed(1)}%`);
                await db.collection("cheat_flags").doc(uid).set({
                    uid,
                    flaggedAt: Date.now(),
                    winRate,
                    battlesIn24h: recent.length,
                    wins,
                    threshold: CONFIG.CHEAT_WIN_RATE_THRESHOLD,
                    minBattles: CONFIG.CHEAT_MIN_BATTLES_24H,
                    reviewed: false,
                }, { merge: true });
            }
        }
    } catch (err) {
        console.error("[resolveMatch] anti-cheat write error:", err);
    }

    return result.cachedResult;
});

exports.getDashboardStats = onRequest(
    { region: 'us-central1', maxInstances: 5 },
    async (req, res) => {
        res.set('Access-Control-Allow-Origin', '*');
        res.set('Access-Control-Allow-Methods', 'GET, OPTIONS');
        if (req.method === 'OPTIONS') { res.status(204).send(''); return; }
        try {
            const admin = require('firebase-admin');
            if (!admin.apps.length) admin.initializeApp();
            const db = admin.firestore();

            // Active capbots + tier distribution + leaderboard + total Cap Coins
            const playersSnap = await db.collection('players')
                .where('stakedTier', '>=', 0).get();
            const tierDist = [0, 0, 0, 0];
            const leaderboard = [];
            let totalCapCoinsDistributed = 0;
            playersSnap.docs.forEach(d => {
                const p = d.data();
                if (typeof p.stakedTier === 'number' && p.stakedTier >= 0 && p.stakedTier <= 3) {
                    tierDist[p.stakedTier]++;
                }
                totalCapCoinsDistributed += (p.totalWon || 0);
                leaderboard.push({
                    wallet: p.solanaWalletAddress || p.playerId,
                    displayName: p.displayName || '',
                    tier: p.stakedTier,
                    brainSteps: p.stakedBrainSteps || 0,
                    totalWon: p.totalWon || 0,
                });
            });
            leaderboard.sort((a, b) => b.totalWon - a.totalWon);

            // Brain upgrades aggregate
            const upgradesSnap = await db.collection('brain_upgrades')
                .orderBy('timestamp', 'desc').limit(500).get();
            let totalBrainStepsAttested = 0;
            let totalCoinsBurned = 0;
            upgradesSnap.docs.forEach(d => {
                const u = d.data();
                totalBrainStepsAttested += ((u.newBrainSteps || 0) - (u.oldBrainSteps || 0));
                totalCoinsBurned += (u.coinsBurned || 0);
            });

            // Capbot activity (battles)
            const dayAgo = Date.now() - 24 * 3600 * 1000;
            const activitySnap = await db.collection('capbot_activity')
                .where('timestamp', '>=', dayAgo).limit(2000).get();
            const battlesToday = activitySnap.size;

            res.json({
                stats: {
                    activeCapbots: playersSnap.size,
                    totalUpgrades: upgradesSnap.size,
                    totalBrainStepsAttested,
                    totalCoinsBurned,
                    totalCapCoinsDistributed,
                    battlesToday,
                },
                tierDist,
                leaderboard: leaderboard.slice(0, 10),
            });
        } catch (err) {
            console.error('[getDashboardStats]', err);
            res.status(500).json({ error: err.message });
        }
    }
);

exports.getBattleReplay = onRequest(
    { region: 'us-central1', maxInstances: 5 },
    async (req, res) => {
        res.set('Access-Control-Allow-Origin', '*');
        res.set('Access-Control-Allow-Methods', 'GET, OPTIONS');
        if (req.method === 'OPTIONS') { res.status(204).send(''); return; }
        try {
            const battleId = req.query.id;
            if (!battleId) { res.status(400).json({ error: 'missing id query param' }); return; }
            const admin = require('firebase-admin');
            if (!admin.apps.length) admin.initializeApp();
            const db = admin.firestore();
            const snap = await db.collection('battle_replays').doc(battleId).get();
            if (!snap.exists) { res.status(404).json({ error: 'replay not found' }); return; }
            const replay = snap.data();
            // Attach matching brain upgrade (proof-of-burn beat in outcome panel)
            const upgradeSnap = await db.collection('brain_upgrades')
                .where('battleId', '==', battleId).limit(1).get();
            if (!upgradeSnap.empty) replay.brainUpgrade = upgradeSnap.docs[0].data();
            res.json(replay);
        } catch (err) {
            console.error('[getBattleReplay]', err);
            res.status(500).json({ error: err.message });
        }
    }
);

exports.getRecentBattles = onRequest(
    { region: 'us-central1', maxInstances: 5 },
    async (req, res) => {
        res.set('Access-Control-Allow-Origin', '*');
        res.set('Access-Control-Allow-Methods', 'GET, OPTIONS');
        if (req.method === 'OPTIONS') { res.status(204).send(''); return; }
        try {
            const limit = Math.min(parseInt(req.query.limit) || 50, 200);
            const admin = require('firebase-admin');
            if (!admin.apps.length) admin.initializeApp();
            const db = admin.firestore();
            const snap = await db.collection('battle_replays')
                .orderBy('timestamp', 'desc').limit(limit).get();
            const battles = snap.docs.map(d => {
                const data = d.data();
                return {
                    battleId: data.battleId,
                    walletAddress: data.walletAddress,
                    capbotName: data.capbotName,
                    capbotTier: data.capbotTier,
                    opponentName: data.opponentName,
                    result: data.result,
                    capCoinDelta: data.capCoinDelta,
                    multiplier: data.multiplier,
                    timestamp: data.timestamp,
                    turnCount: (data.turns || []).length,
                };
            });
            res.json({ battles, count: battles.length });
        } catch (err) {
            console.error('[getRecentBattles]', err);
            res.status(500).json({ error: err.message });
        }
    }
);

exports.getBrainUpgradeProofs = onRequest(
    { region: 'us-central1', maxInstances: 5 },
    async (req, res) => {
        res.set('Access-Control-Allow-Origin', '*');
        res.set('Access-Control-Allow-Methods', 'GET, OPTIONS');
        res.set('Access-Control-Allow-Headers', 'Content-Type');
        if (req.method === 'OPTIONS') { res.status(204).send(''); return; }
        try {
            const admin = require('firebase-admin');
            if (!admin.apps.length) admin.initializeApp();
            const db = admin.firestore();
            const snap = await db.collection('brain_upgrades')
                .orderBy('timestamp', 'desc').limit(500).get();
            const upgrades = snap.docs.map(d => d.data());
            const totalStepsMinted = upgrades.reduce(
                (sum, u) => sum + ((u.newBrainSteps || 0) - (u.oldBrainSteps || 0)), 0);
            const totalCoinsBurned = upgrades.reduce(
                (sum, u) => sum + (u.coinsBurned || 0), 0);
            res.json({ upgrades, totalCount: upgrades.length, totalStepsMinted, totalCoinsBurned });
        } catch (err) {
            console.error('[getBrainUpgradeProofs]', err);
            res.status(500).json({ error: err.message });
        }
    }
);

// ============================================================
// reviveStarter — restore a dead starter from another's coins (§5.3)
// ============================================================
exports.reviveStarter = onCall({
    enforceAppCheck: false,
    cors: ALLOWED_ORIGINS,
}, async (request) => {

    if (!request.auth) {
        throw new HttpsError("unauthenticated", "Must be signed in.");
    }
    const uid = request.auth.uid;

    const { fromStarter, toStarter, amount } = request.data || {};

    if (!Number.isInteger(amount) || amount < CONFIG.REVIVE_MIN_AMOUNT || amount > CONFIG.REVIVE_MAX_AMOUNT) {
        throw new HttpsError("invalid-argument", `Transfer amount must be ${CONFIG.REVIVE_MIN_AMOUNT}-${CONFIG.REVIVE_MAX_AMOUNT} coins.`);
    }
    const validStarters = ["Rageblaze", "Tsunami", "Healspike"];
    if (!validStarters.includes(fromStarter) || !validStarters.includes(toStarter)) {
        throw new HttpsError("invalid-argument", "fromStarter and toStarter must be valid starter names.");
    }
    if (fromStarter === toStarter) {
        throw new HttpsError("invalid-argument", "fromStarter and toStarter must differ.");
    }

    const playerRef = db.collection("players").doc(uid);

    const result = await db.runTransaction(async (tx) => {
        const snap = await tx.get(playerRef);
        if (!snap.exists) {
            throw new HttpsError("not-found", "Player data not found.");
        }
        const data = snap.data();

        const fromKey = `${fromStarter.toLowerCase()}Coins`;
        const toKey = `${toStarter.toLowerCase()}Coins`;

        if ((data[fromKey] ?? 0) < amount) {
            throw new HttpsError("failed-precondition", "Not enough coins on source starter.");
        }
        if ((data[toKey] ?? 0) !== 0) {
            throw new HttpsError("failed-precondition", "Target starter is not dead (must be 0 coins).");
        }

        const updates = {
            [fromKey]: data[fromKey] - amount,
            [toKey]: amount,
            lastUpdated: Date.now(),
        };
        tx.update(playerRef, updates);

        return {
            success: true,
            fromStarter,
            toStarter,
            amount,
            newFromBalance: data[fromKey] - amount,
            newToBalance: amount,
        };
    });

    return result;
});


// ============================================================
// linkWallet — verify Phantom ownership + mirror staked NFT tier
// ============================================================
// Flow:
//   1. Verify Ed25519 signature on the challenge message (proves wallet ownership)
//   2. Sanity-check timestamp + UID inside message (anti-replay, anti-impersonation)
//   3. Query Solana for stake_record accounts owned by walletAddress
//   4. Pick highest-tier stake (in case user has multiple staked)
//   5. Write to players/<uid> via admin SDK (bypasses rules)
//
// Returns: { walletAddress, stakedTier, stakedBrainSteps, stakedAssetId }
// stakedTier = -1 means wallet linked but no NFT staked (or no Capmon stake found).
exports.linkWallet = onCall({
    enforceAppCheck: false,
    cors: ALLOWED_ORIGINS,
}, async (request) => {

    if (!request.auth) {
        throw new HttpsError("unauthenticated", "Must be signed in to link a wallet.");
    }
    const uid = request.auth.uid;

    const { walletAddress, message, signature, timestamp } = request.data || {};
    if (!walletAddress || !message || !signature || !timestamp) {
        throw new HttpsError("invalid-argument", "Missing required fields.");
    }

    // ---------- 1. SIGNATURE VERIFICATION ----------
    let pubkeyBytes;
    try {
        pubkeyBytes = new PublicKey(walletAddress).toBytes();
    } catch (e) {
        throw new HttpsError("invalid-argument", "Invalid wallet address.");
    }

    const messageBytes = new TextEncoder().encode(message);
    const signatureBytes = Uint8Array.from(signature);
    const isValid = nacl.sign.detached.verify(messageBytes, signatureBytes, pubkeyBytes);
    if (!isValid) {
        console.warn(`[linkWallet] Signature verification failed for uid=${uid}`);
        throw new HttpsError("permission-denied", "Signature verification failed.");
    }

    // ---------- 2. ANTI-REPLAY + ANTI-IMPERSONATION ----------
    const ageMs = Date.now() - Number(timestamp);
    if (ageMs > CONFIG.LINK_WALLET_MAX_MESSAGE_AGE_MS || ageMs < -60000) {
        throw new HttpsError("failed-precondition", "Message timestamp out of range.");
    }
    if (!message.includes(`UID: ${uid}`)) {
        throw new HttpsError("permission-denied", "Message UID mismatch.");
    }
    if (!message.includes(`Wallet: ${walletAddress}`)) {
        throw new HttpsError("permission-denied", "Message wallet mismatch.");
    }

    // ---------- 3. QUERY ON-CHAIN STAKE RECORDS ----------
    const connection = new Connection(HELIUS_RPC, "confirmed");
    let accounts;
    try {
        accounts = await connection.getProgramAccounts(STAKING_PROGRAM_ID, {
            filters: [
                { dataSize: STAKE_RECORD_SIZE },
                { memcmp: { offset: STAKE_OWNER_OFFSET, bytes: walletAddress } },
            ],
        });
    } catch (e) {
        console.error("[linkWallet] RPC error:", e);
        throw new HttpsError("unavailable", "Solana RPC error.");
    }

    // ---------- 4. PICK HIGHEST-TIER STAKE ----------
    let bestTier = -1;
    let bestBrainSteps = 0;
    let bestAssetId = null;
    for (const acct of accounts) {
        const data = acct.account.data;
        if (data.length < STAKE_RECORD_SIZE) continue;
        const tier = data[STAKE_TIER_OFFSET];
        const brainSteps = data.readUInt32LE(STAKE_BRAIN_STEPS_OFFSET);
        const assetId = new PublicKey(data.slice(STAKE_ASSET_OFFSET, STAKE_ASSET_OFFSET + 32)).toBase58();
        if (tier > bestTier) {
            bestTier = tier;
            bestBrainSteps = brainSteps;
            bestAssetId = assetId;
        }
    }

    // ---------- 5. WRITE TO FIRESTORE (admin SDK bypasses rules) ----------
    const playerRef = db.collection("players").doc(uid);
    await playerRef.update({
        solanaWalletAddress: walletAddress,
        stakedTier: bestTier,
        stakedBrainSteps: bestBrainSteps,
        stakedAssetId: bestAssetId,
        walletLinkedAt: Date.now(),
    });

    console.log(`[linkWallet] uid=${uid} wallet=${walletAddress.slice(0, 4)}... tier=${bestTier} steps=${bestBrainSteps}`);

    return {
        walletAddress,
        stakedTier: bestTier,
        stakedBrainSteps: bestBrainSteps,
        stakedAssetId: bestAssetId,
    };
});

// ============================================================
// signInWithWallet — wallet-only auth path for hackathon judges
// ============================================================
// Verify a Phantom-signed challenge, mint a Firebase custom token with
// UID = wallet pubkey, create or refresh the player doc, and return the
// custom token. Client then calls signInWithCustomToken(auth, token) and
// the existing loadPlayerData path takes over.
//
// This is parallel to (NOT replacing) the X auth flow. Existing X-signed-in
// players are unaffected. A wallet that signs in here gets its own player
// doc — separate balances from any X-linked account that happens to share
// the same wallet. Acceptable hackathon limitation.
//
// Flow:
//   1. Verify Ed25519 signature on the challenge message
//   2. Anti-replay (timestamp <= 5 min old) + anti-context (message must
//      contain the walletAddress to prevent sig reuse)
//   3. Query Solana for stake_records owned by the wallet
//   4. Create or update players/<walletPubkey> doc with stake state mirrored
//   5. Mint Firebase custom token with UID = wallet pubkey
//   6. Return { customToken, walletAddress, stakedTier, stakedBrainSteps,
//               stakedAssetId, isNewPlayer }
exports.signInWithWallet = onCall({
    enforceAppCheck: false,
    cors: ALLOWED_ORIGINS,
}, async (request) => {

    const { walletAddress, message, signature, timestamp } = request.data || {};
    if (!walletAddress || !message || !signature || !timestamp) {
        throw new HttpsError("invalid-argument", "Missing required fields.");
    }

    // ---------- 1. SIGNATURE VERIFICATION ----------
    let pubkeyBytes;
    try {
        pubkeyBytes = new PublicKey(walletAddress).toBytes();
    } catch (e) {
        throw new HttpsError("invalid-argument", "Invalid wallet address.");
    }

    const messageBytes = new TextEncoder().encode(message);
    const signatureBytes = Uint8Array.from(signature);
    const isValid = nacl.sign.detached.verify(messageBytes, signatureBytes, pubkeyBytes);
    if (!isValid) {
        console.warn(`[signInWithWallet] Sig verify failed for wallet=${walletAddress.slice(0,8)}...`);
        throw new HttpsError("permission-denied", "Signature verification failed.");
    }

    // ---------- 2. ANTI-REPLAY + ANTI-CONTEXT ----------
    const ageMs = Date.now() - Number(timestamp);
    if (ageMs > CONFIG.LINK_WALLET_MAX_MESSAGE_AGE_MS || ageMs < -60000) {
        throw new HttpsError("failed-precondition", "Message timestamp out of range.");
    }
    if (!message.includes(`Wallet: ${walletAddress}`)) {
        throw new HttpsError("permission-denied", "Message wallet mismatch.");
    }

    // ---------- 3. QUERY ON-CHAIN STAKE RECORDS ----------
    // Same memcmp logic as linkWallet — extracts highest-tier stake for this wallet.
    const connection = new Connection(HELIUS_RPC, "confirmed");
    let accounts;
    try {
        accounts = await connection.getProgramAccounts(STAKING_PROGRAM_ID, {
            filters: [
                { dataSize: STAKE_RECORD_SIZE },
                { memcmp: { offset: STAKE_OWNER_OFFSET, bytes: walletAddress } },
            ],
        });
    } catch (e) {
        console.error("[signInWithWallet] RPC error:", e);
        throw new HttpsError("unavailable", "Solana RPC error.");
    }

    let bestTier = -1;
    let bestBrainSteps = 0;
    let bestAssetId = null;
    for (const acct of accounts) {
        const data = acct.account.data;
        if (data.length < STAKE_RECORD_SIZE) continue;
        const tier = data[STAKE_TIER_OFFSET];
        const brainSteps = data.readUInt32LE(STAKE_BRAIN_STEPS_OFFSET);
        const assetId = new PublicKey(data.slice(STAKE_ASSET_OFFSET, STAKE_ASSET_OFFSET + 32)).toBase58();
        if (tier > bestTier) {
            bestTier = tier;
            bestBrainSteps = brainSteps;
            bestAssetId = assetId;
        }
    }

    // ---------- 4. CREATE OR REFRESH PLAYER DOC ----------
    // Admin SDK bypasses Firestore rules — we can write the wallet-derived
    // initial doc with stake fields populated from the start.
    const playerRef = db.collection("players").doc(walletAddress);
    const snap = await playerRef.get();
    const isNewPlayer = !snap.exists;

    if (isNewPlayer) {
        await playerRef.set({
            playerId: walletAddress,
            displayName: `Wallet ${walletAddress.slice(0, 4)}..${walletAddress.slice(-4)}`,
            isGuest: false,
            currentStarter: 'Rageblaze',
            rageblazeCoins: 50000,
            tsunamiCoins: 50000,
            healspikeCoins: 50000,
            aiRageblazeCoins: 10000000,
            aiTsunamiCoins: 10000000,
            aiHealspikeCoins: 10000000,
            totalWon: 0,
            rank: 0,
            createdAt: Date.now(),
            solanaWalletAddress: walletAddress,
            stakedTier: bestTier,
            stakedBrainSteps: bestBrainSteps,
            stakedAssetId: bestAssetId,
            walletLinkedAt: Date.now(),
            authMethod: 'wallet',  // tag for analytics — distinguishes from 'twitter' path
        });
    } else {
        // Returning wallet — refresh stake state but don't touch balances.
        await playerRef.update({
            solanaWalletAddress: walletAddress,
            stakedTier: bestTier,
            stakedBrainSteps: bestBrainSteps,
            stakedAssetId: bestAssetId,
            walletLinkedAt: Date.now(),
        });
    }

    // ---------- 5. MINT FIREBASE CUSTOM TOKEN ----------
    // UID = wallet pubkey directly. Base58 is ASCII-safe and well under
    // Firebase's 128-char UID limit.
    const { getAuth } = require('firebase-admin/auth');
    const customToken = await getAuth().createCustomToken(walletAddress);

    console.log(`[signInWithWallet] ${isNewPlayer ? 'NEW' : 'returning'} wallet=${walletAddress.slice(0,8)}... tier=${bestTier} steps=${bestBrainSteps}`);

    return {
        customToken,
        walletAddress,
        stakedTier: bestTier,
        stakedBrainSteps: bestBrainSteps,
        stakedAssetId: bestAssetId,
        isNewPlayer,
    };
});

// ============================================================
// getCapbotData — read-only data for the Unity Capbot tab UI
// ============================================================
// Returns:
//   - Player stake state mirrored from on-chain (tier, brainSteps, assetId, wallet)
//   - Last 20 autonomous battles run by the Capbot server (capbot_activity)
//   - Last 10 on-chain Pattern 2 brain-upgrade tx signatures (brain_upgrades)
//
// Both subqueries require composite indexes (uid asc + timestamp desc). Firestore
// will print a clickable index-create link in the function logs the first time
// each query runs — click both to provision them. ~30s to build then ready.
//
// No writes, no transactions, no idempotency — pure read fan-out, safe to call
// frequently (e.g. on tab open + Refresh button).
exports.getCapbotData = onCall({
    enforceAppCheck: false,
    cors: ALLOWED_ORIGINS,
}, async (request) => {

    if (!request.auth) {
        throw new HttpsError("unauthenticated", "Must be signed in.");
    }
    const uid = request.auth.uid;

    // ---------- 1. PLAYER STAKE STATE ----------
    const playerSnap = await db.collection("players").doc(uid).get();
    if (!playerSnap.exists) {
        throw new HttpsError("not-found", "Player not found.");
    }
    const player = playerSnap.data();

    // ---------- 2. RECENT AUTONOMOUS BATTLES ----------
    // Most recent first. capbot-server writes these in runBattleForPlayer().
    const battlesSnap = await db.collection("capbot_activity")
        .where("uid", "==", uid)
        .orderBy("timestamp", "desc")
        .limit(20)
        .get();

    // ---------- 3. RECENT ON-CHAIN BRAIN UPGRADES ----------
    // Most recent first. capbot-server writes these in upgradeBrainOnChain()
    // after a successful Ed25519-verified upgrade_brain_v2 tx.
    const upgradesSnap = await db.collection("brain_upgrades")
        .where("uid", "==", uid)
        .orderBy("timestamp", "desc")
        .limit(10)
        .get();

    // ---------- 4. SHAPE RESPONSE FOR UNITY (JsonUtility-compatible) ----------
    // Field names match CapbotData / CapbotBattleEntry / BrainUpgradeEntry
    // [Serializable] classes in FirebaseManager.cs. Don't rename fields here
    // without updating the C# side (silent JsonUtility default failure).
    return {
        stakedTier: player.stakedTier ?? -1,
        stakedBrainSteps: player.stakedBrainSteps ?? 0,
        stakedAssetId: player.stakedAssetId ?? null,
        walletAddress: player.solanaWalletAddress ?? null,
        recentBattles: battlesSnap.docs.map(d => {
            const x = d.data();
            return {
                timestamp: x.timestamp,
                battleId: x.battleId,
                capbotType: x.capbotType,
                defeatedAi: x.defeatedAi,
                payout: x.payout,
                multiplier: x.multiplier,
                playerWon: x.playerWon,
            };
        }),
        recentUpgrades: upgradesSnap.docs.map(d => {
            const x = d.data();
            return {
                timestamp: x.timestamp,
                battleId: x.battleId,
                oldBrainSteps: x.oldBrainSteps,
                newBrainSteps: x.newBrainSteps,
                txSignature: x.txSignature,
            };
        }),
    };
});


// ============================================================
// cleanupIdempotencyKeys — daily cleanup of expired keys
// ============================================================
exports.cleanupIdempotencyKeys = onSchedule({
    schedule: "every day 03:00",
    region: "us-central1",
    timeZone: "America/Los_Angeles",
}, async () => {
    const now = Date.now();
    const snap = await db.collection("battle_idempotency_keys")
        .where("expiresAt", "<", now)
        .limit(500)
        .get();

    if (snap.empty) {
        console.log("[cleanupIdempotencyKeys] no expired keys");
        return;
    }

    const batch = db.batch();
    snap.docs.forEach(doc => batch.delete(doc.ref));
    await batch.commit();
    console.log(`[cleanupIdempotencyKeys] deleted ${snap.size} expired keys`);
});
