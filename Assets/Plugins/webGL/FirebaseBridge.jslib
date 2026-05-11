// FirebaseBridge.jslib
// Location: Assets/Plugins/WebGL/FirebaseBridge.jslib

mergeInto(LibraryManager.library, {

    FirebaseInitJS: function() {
        if (typeof window.CapmonFirebase !== 'undefined' && window.CapmonFirebase.init) {
            window.CapmonFirebase.init();
        } else {
            console.error('[FirebaseBridge] window.CapmonFirebase not defined - check index.html');
        }
    },

    SignInWithTwitterJS: function() {
        if (typeof window.CapmonFirebase !== 'undefined' && window.CapmonFirebase.signInWithTwitter) {
            window.CapmonFirebase.signInWithTwitter();
        } else {
            console.error('[FirebaseBridge] window.CapmonFirebase.signInWithTwitter not defined');
            SendMessage('FirebaseManager', 'OnTwitterSignInError', 'JS bridge not initialized');
        }
    },

    SignInWithWalletJS: function() {
    if (typeof window.CapmonFirebase !== 'undefined' && window.CapmonFirebase.signInWithWallet) {
        window.CapmonFirebase.signInWithWallet();
    } else {
        SendMessage('FirebaseManager', 'OnWalletSignInError', 'Bridge not initialized');
    }
},

    LoadPlayerDataJS: function(playerIdPtr) {
        var playerId = UTF8ToString(playerIdPtr);
        if (typeof window.CapmonFirebase !== 'undefined' && window.CapmonFirebase.loadPlayerData) {
            window.CapmonFirebase.loadPlayerData(playerId);
        } else {
            console.error('[FirebaseBridge] window.CapmonFirebase.loadPlayerData not defined');
            SendMessage('FirebaseManager', 'OnTwitterSignInError', 'JS bridge not initialized');
        }
    },

    SavePlayerDataJS: function(jsonPtr) {
        var json = UTF8ToString(jsonPtr);
        if (typeof window.CapmonFirebase !== 'undefined' && window.CapmonFirebase.savePlayerData) {
            window.CapmonFirebase.savePlayerData(json);
        } else {
            console.error('[FirebaseBridge] window.CapmonFirebase.savePlayerData not defined');
            SendMessage('FirebaseManager', 'OnSavePlayerDataError', 'JS bridge not initialized');
        }
    },

    ResolveMatchJS: function(jsonPtr) {
        var json = UTF8ToString(jsonPtr);
        if (typeof window.CapmonFirebase !== 'undefined' && window.CapmonFirebase.resolveMatch) {
            window.CapmonFirebase.resolveMatch(json);
        } else {
            console.error('[FirebaseBridge] window.CapmonFirebase.resolveMatch not defined');
            SendMessage('FirebaseManager', 'OnMatchResolveError', 'JS bridge not initialized');
        }
    },

    ReviveStarterJS: function(jsonPtr) {
        var json = UTF8ToString(jsonPtr);
        if (typeof window.CapmonFirebase !== 'undefined' && window.CapmonFirebase.reviveStarter) {
            window.CapmonFirebase.reviveStarter(json);
        } else {
            console.error('[FirebaseBridge] window.CapmonFirebase.reviveStarter not defined');
            SendMessage('FirebaseManager', 'OnReviveError', 'JS bridge not initialized');
        }
    },

    GetCapbotDataJS: function() {
        if (typeof window.CapmonFirebase !== 'undefined' && window.CapmonFirebase.getCapbotData) {
            window.CapmonFirebase.getCapbotData();
        } else {
            SendMessage('FirebaseManager', 'OnCapbotDataError', 'Bridge not initialized');
        }
    }
});
