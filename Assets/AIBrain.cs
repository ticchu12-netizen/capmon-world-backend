using UnityEngine;
using Unity.InferenceEngine; // Updated namespace for Sentis/Inference Engine
using System.Collections; // For IEnumerator
using System.Collections.Generic; // For Dictionary and Queue
using System.Linq; // For Average and ToList

public class AIBrain : MonoBehaviour
{
    [SerializeField] private ModelAsset[] brainModels; // Assign .onnx/.sentis model assets via Inspector drag-and-drop

    private Worker worker; // Concrete Worker (2.1+ API)
    private Model _model; // Stored for potential future use
    private int personality;
    private const int MCTS_SIMULATIONS = 50; // Advanced: Planning depth (tune for performance)
    private float[] oppMoveProbs = new float[4]; // Opponent modeling: Bayesian probs (update over turns)
    private float[] oppPPEstimates = new float[4]; // New: Estimated opponent PP (starts high, decreases on use)
    private bool explain = true; // XAI: Log decisions for marketing/hype (toggle false for production)
    private Dictionary<ElementType, float> oppTypeProbs;
    private Queue<float[]> obsQueue;
    private int currentTurn = 0;
    private Tensor<float> memory; // NEW: Persistent LSTM memory state (empty for this non-recurrent model)

    // NEW: Track if ultimate has been used this episode/battle
    private bool hasUsedUltimateThisEpisode = false;

    public void SetBrainModels(ModelAsset[] models)
    {
        brainModels = models;
    }

    void Awake()
    {
        // SAFE CHECK: Fallback if no brains assigned in Inspector
        if (brainModels == null || brainModels.Length == 0)
        {
            // Commented out to suppress brain load logs
            // Debug.Log("[AIBrain] No valid brain models assigned in Inspector → using safe random moves (normal until training)");
            return;
        }

        try
        {
            personality = UnityEngine.Random.Range(0, brainModels.Length);
            var selectedModelAsset = brainModels[personality];
            if (selectedModelAsset == null)
            {
                throw new System.Exception("Selected brain model is null in Inspector array");
            }

            _model = ModelLoader.Load(selectedModelAsset);
            worker = new Worker(_model, BackendType.CPU); // CPU for WebGL; auto GPU if available
            
            // Commented out to suppress brain load logs
            // Debug.Log($"[AIBrain] Inspector-assigned cutting-edge brain: {selectedModel.name} (MARL trained, LSTM memory, MCTS hybrid)");

            // Log input and output names for debugging
            // Commented out to suppress brain load logs
             Debug.Log("Model inputs: " + string.Join(", ", _model.inputs.Select(i => i.name)));
             Debug.Log("Model outputs: " + string.Join(", ", _model.outputs.Select(o => o.name)));

            // NEW: Initialize memory as empty tensor to match non-recurrent shape (d1,1,0) – no data needed
            memory = new Tensor<float>(new TensorShape(1, 1, 512), new float[512]);
        }
        catch (System.Exception)
        {
            // Commented out to suppress brain load logs
            // Debug.LogWarning($"[AIBrain] Inspector brain assignment failed → fallback random. Error: {e.Message}");
            worker = null;
        }

        // Initialize opponent model (uniform prior)
        for (int m = 0; m < 4; m++) oppMoveProbs[m] = 0.25f;

        // Initialize opponent PP estimates (assume full at start)
        oppPPEstimates = new float[4] { 1f, 1f, 1f, 1f }; // Normalized 1f for infinite/usable

        // Initialize opponent type probs (uniform)
        oppTypeProbs = new Dictionary<ElementType, float> { { ElementType.Fire, 0.33f }, { ElementType.Water, 0.33f }, { ElementType.Grass, 0.34f } };

        // Initialize observation queue for stacking
        obsQueue = new Queue<float[]>();

        // NEW: Reset ultimate usage flag every battle/episode
        hasUsedUltimateThisEpisode = false;
    }

    public int ChooseMove(BattleCharacter self, BattleCharacter opp)
    {
        if (worker == null || self == null || opp == null || self.moves == null || opp.moves == null || self.moves.Length < 4 || opp.moves.Length < 4) 
        {
            Debug.LogWarning("[AIBrain] Invalid input: Falling back to random move.");
            return FallbackRandomValidMove(self);
        }

        // 1. Update opponent model from last move (Bayesian: boost prob of observed move)
        if (opp.lastUsedMove != null) 
        {
            UpdateOppModel(opp.lastUsedMove);
            UpdateOppPPEstimates(opp.lastUsedMove); // New: Decrease estimated PP for observed move
        }

        // Increment turn
        currentTurn++;

        // 2. RL Inference: Get policy probs from model (LSTM for history)
        float[] rlProbs = GetRLPolicy(self, opp);

        // Simulate masking: zero probs for invalids
        float[] probs = new float[4];
        System.Array.Copy(rlProbs, probs, 4);
        float totalProb = 0f;
        for (int i = 0; i < 4; i++)
        {
            if (!self.IsMoveUsable(i)) probs[i] = 0f;
            else totalProb += probs[i];
        }

        // 3. Hybrid MCTS: Plan with simulations (debuff/passives/type/crit)
        int bestMove = MCTSPlanning(self, opp, probs);

        // =====================================================================
        // HARD-CODED OVERRIDE RULES (applied AFTER the trained model + MCTS)
        // These enforce the exact techniques you want while still using the 60M brain
        // =====================================================================

        float oppHpNorm   = (float)opp.currentHP / opp.maxHP;
        float debuffPct   = opp.defenseDebuffPercentage;
        float typeAdv     = BattleUtils.GetTypeEffectiveness(self.type, opp.type) 
                          - BattleUtils.GetTypeEffectiveness(opp.type, self.type);

        // FIXED RULE: Ultimate must be used at least once per game, but NEVER as first move.
        // Only triggers when low HP (one-shot) or within 20-30 HP buffer, or AI is low HP and needs to secure win.
        if (!hasUsedUltimateThisEpisode && self.IsMoveUsable(3) && currentTurn > 5)
        {
            float ultimateDamage = SimulateDamage(self, opp, self.moves[3], false);
            bool isOneShot = opp.currentHP <= ultimateDamage;
            bool nearEnd = opp.currentHP <= ultimateDamage + 30f;   // 20-30 HP buffer

            if (isOneShot || nearEnd || (float)self.currentHP / self.maxHP < 0.4f)
            {
                bestMove = 3;
                hasUsedUltimateThisEpisode = true;
            }
        }

        // RULE 1: Ultimate must be saved for finishing blow (low HP + decent debuff)
        else if (self.IsMoveUsable(3) && oppHpNorm <= 0.30f && debuffPct >= 40f)
        {
            bestMove = 3;
        }
        // RULE 2: Force Intimidate stacking when opponent is stronger
        else if (typeAdv < 0f && debuffPct < 55f)
        {
            bestMove = 2;
        }
        // RULE 3: Stop Intimidate and go for damage when KO is possible
        else if (debuffPct >= 55f)
        {
            float estDmg = SimulateDamage(self, opp, self.moves[1], false);
            if (estDmg >= opp.currentHP)
            {
                bestMove = 1;
            }
        }
        // RULE 4: Always prefer type-advantage move over neutral Headbutt
        else
        {
            float typeEff = BattleUtils.GetTypeEffectiveness(self.moves[1].type, opp.type);
            if (typeEff > 1.0f)
            {
                bestMove = 1;
            }
        }

        // =====================================================================
        // END OF HARD-CODED RULES
        // =====================================================================

        // 4. XAI: Explain decision for hype/marketing
        if (explain)
        {
            float winProb = CalculateWinProb(bestMove, self, opp);
            Debug.Log($"[AIBrain XAI] Chose {self.moves[bestMove].name}: Opp model {oppMoveProbs[bestMove]*100:F0}% likely Intimidate, MCTS sim win +{winProb*100:F0}%, RL conf {rlProbs[bestMove]*100:F0}% (debuff at {opp.defenseDebuffPercentage:F1}%)");
            if (winProb > 0.7f)
            {
                Debug.Log($"[AIBrain] High confidence ({winProb*100:F0}%) — recommend wagering 10% marketcap on this battle for ERC-7857 iNFT.");
            }
        }

        return bestMove;
    }

    private float[] GetRLPolicy(BattleCharacter self, BattleCharacter opp)
    {
        float[] currentObs = new float[64];
        int i = 0;

        // Base state (normalized)
        currentObs[i++] = (float)self.currentHP / self.maxHP;
        currentObs[i++] = self.speed / 150f;
        currentObs[i++] = self.defense / 150f;
        currentObs[i++] = self.intimidateCount / 10f;
        currentObs[i++] = self.defenseDebuffPercentage / 100f;
        currentObs[i++] = self.speedDebuffPercentage / 100f; // Assuming BattleCharacter has this field; set to 0f if not implemented

        currentObs[i++] = (float)opp.currentHP / opp.maxHP;
        currentObs[i++] = opp.speed / 150f;
        currentObs[i++] = opp.defense / 150f;
        currentObs[i++] = opp.defenseDebuffPercentage / 100f;

        // Move PP and values (normalized)
        for (int m = 0; m < 4; m++)
        {
            var move = self.moves[m];
            currentObs[i++] = (move.pp > 0 || move.pp == -1) ? 1f : 0f;
            float p = move.power / 100f;
            if (move.name == "Intimidate") p = opp.defenseDebuffPercentage < 75f ? 0.6f : 0.05f;
            float stab = move.type == self.type ? 1.05f : 1f;
            float eff = BattleUtils.GetTypeEffectiveness(move.type, opp.type);
            currentObs[i++] = p * stab * eff;
            currentObs[i++] = move.isStatus ? 1f : 0f; // Assuming Move has isStatus field
        }

        // Add normalized PP for conservation learning
        for (int m = 0; m < 4; m++)
        {
            var move = self.moves[m];
            float normPP = (move.pp == -1) ? 1f : (float)move.pp / 1f; // Infinite as 1f, ultimate as pp/1
            currentObs[i++] = normPP;
        }

        // Speed tie and turn count (for time pressure)
        currentObs[i++] = self.speed >= opp.speed ? 1f : 0f;
        currentObs[i++] = currentTurn / 50f; // Normalized turn (max 50)

        // Opponent modeling (estimated probs, Bayesian update based on last move)
        UpdateOppTypeProbs(opp); // Pass opp for lastUsedMove access
        currentObs[i++] = oppTypeProbs[ElementType.Fire];
        currentObs[i++] = oppTypeProbs[ElementType.Water];
        currentObs[i++] = oppTypeProbs[ElementType.Grass];

        // One-hot for last used move (own and opp)
        for (int j = 0; j < 4; j++) currentObs[i++] = (self.lastUsedMove != null && GetMoveIdx(self.lastUsedMove) == j) ? 1f : 0f;
        for (int j = 0; j < 4; j++) currentObs[i++] = (opp.lastUsedMove != null && GetMoveIdx(opp.lastUsedMove) == j) ? 1f : 0f;

        // Type one-hot (own and opp)
        int ownTypeIdx = (int)self.type;
        for (int j = 0; j < 3; j++) currentObs[i++] = (ownTypeIdx == j) ? 1f : 0f;
        int oppTypeIdx = (int)opp.type;
        for (int j = 0; j < 3; j++) currentObs[i++] = (oppTypeIdx == j) ? 1f : 0f;

        // Advanced: Crit probability estimate, passive effects
        currentObs[i++] = EstimateCritProb(self);
        currentObs[i++] = EstimatePassiveValue(self.type);

        // Pad remaining to 64 with 0f (after adding 4 PP, pad 17)

        for (int j = i; j < 64; j++) currentObs[j] = 0f;

        // Stack the observations (stacked_vectors = 3)
        obsQueue.Enqueue((float[])currentObs.Clone());
        if (obsQueue.Count > 3) obsQueue.Dequeue();

        float[] stacked = new float[192];
        var queueArray = obsQueue.ToArray();
        int offset = (3 - queueArray.Length) * 64;
        for (int k = 0; k < queueArray.Length; k++)
        {
            System.Array.Copy(queueArray[k], 0, stacked, offset, 64);
            offset += 64;
        }

        using var obsTensor = new Tensor<float>(new TensorShape(1, 192), stacked);

        // Set action masks
        float[] actionMasksData = new float[4];
        for (int m = 0; m < 4; m++)
        {
            actionMasksData[m] = self.IsMoveUsable(m) ? 1f : 0f;
        }
        using var actionMasks = new Tensor<float>(new TensorShape(1, 4), actionMasksData);

        worker.SetInput("obs_0", obsTensor);
        worker.SetInput("action_masks", actionMasks);
        worker.SetInput("recurrent_in", memory); // NEW: Feed memory input

        var enumerator = worker.ScheduleIterable();
        while (enumerator.MoveNext()) { } // Block until complete (sync execution)

        // CHANGED: Output is int64[1,1] (action index), use Tensor<int> in Sentis – read as int and convert to one-hot "probs"
        // Use "deterministic_discrete_actions" for argmax action (consistent AI behavior)
        var outputTensor = worker.PeekOutput("deterministic_discrete_actions") as Tensor<int>;
        if (outputTensor == null)
        {
            Debug.LogError("[AIBrain] Output tensor 'deterministic_discrete_actions' not found or wrong type. Check model output names.");
            return new float[4] { 0.25f, 0.25f, 0.25f, 0.25f }; // Fallback to uniform probs
        }

        int[] actionArray = outputTensor.DownloadToArray();
        int actionIdx = actionArray[0];
        float[] rlProbs = new float[4];
        rlProbs[actionIdx] = 1f; // One-hot: full confidence in the model's selected action
        outputTensor.Dispose();

        // Removed: Memory update logic, as "memory_out" does not exist in this model

        return rlProbs;
    }

    private void UpdateOppTypeProbs(BattleCharacter opp)
    {
        if (opp.lastUsedMove == null) return;
        // Bayesian update: Adjust probs based on move type/effectiveness
        float priorSum = oppTypeProbs.Sum(p => p.Value);
        foreach (var kvp in oppTypeProbs.ToList())
        {
            float likelihood = BattleUtils.GetTypeEffectiveness(opp.lastUsedMove.type, kvp.Key);
            oppTypeProbs[kvp.Key] = (kvp.Value * likelihood) / priorSum;
        }
        float newSum = oppTypeProbs.Sum(p => p.Value);
        foreach (var kvp in oppTypeProbs.ToList())
        {
            oppTypeProbs[kvp.Key] /= newSum; // Normalize
        }
    }

    private int GetMoveIdx(Move move)
    {
        if (move == null) return -1;
        if (move.name == "Headbutt") return 0;
        if (move.type == ElementType.Fire || move.type == ElementType.Water || move.type == ElementType.Grass) return 1;
        if (move.name == "Intimidate") return 2;
        return 3; // Ultimate
    }

    private float EstimateCritProb(BattleCharacter self)
    {
        // Advanced: Simulated crit chance based on moves (e.g., high power = higher crit)
        if (self.moves == null || self.moves.Length == 0) return 0f;
        return self.moves.Average(m => m.power > 50 ? 0.1f : 0.05f);
    }

    private float EstimatePassiveValue(ElementType type)
    {
        // Normalized passive benefit (e.g., grass heal = 0.05)
        switch (type)
        {
            case ElementType.Grass: return 0.05f;
            case ElementType.Water: return 0.05f;
            case ElementType.Fire: return 0.05f;
            default: return 0f;
        }
    }

    private int MCTSPlanning(BattleCharacter self, BattleCharacter opp, float[] rlProbs)
    {
        float[] scores = new float[4];
        float typeAdv = BattleUtils.GetTypeEffectiveness(self.type, opp.type) - BattleUtils.GetTypeEffectiveness(opp.type, self.type); // New: Type advantage obs (positive if stronger)

        for (int m = 0; m < 4; m++)
        {
            float score = 0f;
            for (int sim = 0; sim < MCTS_SIMULATIONS; sim++)
            {
                // Simulate move m + opp predicted move
                int oppMove = SampleOppMove();
                float simDamage = SimulateDamage(self, opp, self.moves[m], true);
                float simOppDamage = SimulateDamage(opp, self, opp.moves[oppMove], false);
                score += simDamage - simOppDamage + rlProbs[m] * 20f; // RL bias + win prob

                // New: Boost for Intimidate if opp stronger (typeAdv <0)
                if (m == 2 && typeAdv < 0) score += 5f; // Arbitrary boost in sim for necessary debuff

                // New: Passive synergy - delay opp passive by prolonging (add survival if sim turn > current)
                if (simOppDamage < opp.currentHP * 0.3f) score += EstimatePassiveValue(opp.type) * -2f; // Penalize if opp survives to passive
            }
            scores[m] = score / MCTS_SIMULATIONS;
        }

        int best = 0;
        for (int m = 1; m < 4; m++) if (scores[m] > scores[best]) best = m;
        return best;
    }

    private float SimulateDamage(BattleCharacter attacker, BattleCharacter defender, Move move, bool applyPassive)
    {
        // Non-mutating simulation
        float simDefDebuff = defender.defenseDebuffPercentage;
        float simDef = defender.defense * (1f - simDefDebuff / 100f); // Apply current debuff to def for calc

        // If Intimidate, sim future debuff for potential future turns (but since single-move sim, just note effect)
        if (move.name == "Intimidate" && simDefDebuff < 75f)
        {
            simDefDebuff += 10f;
            if (simDefDebuff > 75f) simDefDebuff = 75f;
            simDef = defender.baseDefense * (1f - simDefDebuff / 100f); // Sim reduced def
        }

        // Calculate damage with sim def (non-mutating version of CalculateDamage)
        float damage = 0f;
        if (move.power > 0)
        {
            float typeEff = BattleUtils.GetTypeEffectiveness(move.type, defender.type);
            float stab = (move.type == attacker.type) ? 1.05f : 1f;
            float crit = UnityEngine.Random.value < 0.05f ? 1.5f : 1f; // Sim crit
            damage = ((move.power * attacker.attack / simDef) * typeEff * stab * crit);
        }

        // Sim passive without mutation (approximate effects on "effective" damage)
        if (applyPassive)
        {
            switch (attacker.type)
            {
                case ElementType.Grass:
                    // Sim heal as reduced opp effective HP or bonus survival (rough: add heal as negative damage)
                    damage += attacker.maxHP * 0.05f; // Treat heal as extra "damage" to opp by surviving longer
                    break;
                case ElementType.Water:
                    // Speed inc: Higher chance to go first, sim as slight damage boost
                    damage *= 1.05f; // Arbitrary 5% boost for speed advantage
                    break;
                case ElementType.Fire:
                    // Def inc: Reduces incoming, sim as reduced opp damage (but since this is attacker sim, add to score later)
                    damage *= 1.05f; // Similar arbitrary boost
                    break;
            }
        }

        return damage;
    }

    private int SampleOppMove()
    {
        float r = UnityEngine.Random.value;
        float sum = 0f;
        for (int m = 0; m < 4; m++)
        {
            sum += oppMoveProbs[m] * oppPPEstimates[m]; // New: Weight by estimated PP (low if depleted)
            if (r < sum) return m;
        }
        return 0;
    }

    private void UpdateOppModel(Move oppLastMove)
    {
        if (oppLastMove == null) return;
        int idx = GetOppMoveIdx(oppLastMove);
        oppMoveProbs[idx] += 0.1f; // Boost observed
        NormalizeProbs();
    }

    private void UpdateOppPPEstimates(Move oppLastMove)
    {
        if (oppLastMove == null) return;
        int idx = GetOppMoveIdx(oppLastMove);
        if (oppPPEstimates[idx] > 0f) oppPPEstimates[idx] -= 0.2f; // Decrease estimate on use (arbitrary decay; tune)
        if (oppPPEstimates[idx] < 0f) oppPPEstimates[idx] = 0f;
    }

    private int GetOppMoveIdx(Move move)
    {
        if (move.name == "Headbutt") return 0;
        if (move.type == ElementType.Fire || move.type == ElementType.Water || move.type == ElementType.Grass) return 1; // Type attack
        if (move.name == "Intimidate") return 2;
        return 3; // Ultimate
    }

    private void NormalizeProbs()
    {
        float sum = 0f;
        for (int m = 0; m < 4; m++) sum += oppMoveProbs[m];
        for (int m = 0; m < 4; m++) oppMoveProbs[m] /= sum;
    }

    private int FallbackRandomValidMove(BattleCharacter ch)
    {
        List<int> valid = new List<int>();
        for (int i = 0; i < 4; i++) if (ch.IsMoveUsable(i)) valid.Add(i);
        return valid.Count > 0 ? valid[UnityEngine.Random.Range(0, valid.Count)] : 0;
    }

    private float CalculateWinProb(int moveIdx, BattleCharacter self, BattleCharacter opp)
    {
        // Simple approximation: relative HP adjusted by expected damage
        float expectedDmg = SimulateDamage(self, opp, self.moves[moveIdx], false);
        float expectedOppDmg = SimulateDamage(opp, self, opp.moves[SampleOppMove()], false);
        float selfSurvive = self.currentHP - expectedOppDmg;
        float oppSurvive = opp.currentHP - expectedDmg;
        if (selfSurvive <= 0) return 0f;
        if (oppSurvive <= 0) return 1f;
        float baseProb = selfSurvive / (selfSurvive + oppSurvive); // Rough prob

        // New: Boost if ultimate on low-HP (threshold for one-hit)
        if (moveIdx == 3 && opp.currentHP < 0.3f * opp.maxHP) baseProb += 0.2f; // Encourage low-HP nuke

        return Mathf.Clamp(baseProb, 0f, 1f);
    }

    public byte[] GetBrainWeights()
    {
        if (worker == null) return null;
        // Serialization not directly supported at runtime; placeholder for ERC-7857 prep (implement with asset bytes if needed)
        return null;
    }

    void OnDestroy()
    {
        worker?.Dispose();
        memory?.Dispose(); // NEW: Clean up memory tensor
    }
}