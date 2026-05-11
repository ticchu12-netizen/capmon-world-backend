mergeInto(LibraryManager.library, {

    SolanaInitJS: function () {
        try {
            if (window.CapmonSolana && window.CapmonSolana.init) {
                window.CapmonSolana.init();
            } else {
                console.error("[SolanaBridge] window.CapmonSolana.init not found");
                SendMessage('WalletManager', 'OnSolanaError', 'init_unavailable');
            }
        } catch (e) {
            console.error("[SolanaBridge] init error:", e);
            SendMessage('WalletManager', 'OnSolanaError', String(e));
        }
    },

    IsPhantomInstalledJS: function () {
        try {
            return (window.CapmonSolana && window.CapmonSolana.isPhantomInstalled())
                ? 1 : 0;
        } catch (e) {
            return 0;
        }
    },

    ConnectWalletJS: function () {
        try {
            window.CapmonSolana.connect()
                .then(function (pubkey) {
                    SendMessage('WalletManager', 'OnWalletConnected', pubkey);
                })
                .catch(function (err) {
                    SendMessage('WalletManager', 'OnWalletError', String(err.message || err));
                });
        } catch (e) {
            SendMessage('WalletManager', 'OnWalletError', String(e));
        }
    },

    DisconnectWalletJS: function () {
        try {
            window.CapmonSolana.disconnect()
                .then(function () {
                    SendMessage('WalletManager', 'OnWalletDisconnected', '');
                })
                .catch(function (err) {
                    SendMessage('WalletManager', 'OnWalletError', String(err.message || err));
                });
        } catch (e) {
            SendMessage('WalletManager', 'OnWalletError', String(e));
        }
    },

    LinkWalletJS: function (walletPtr) {
        try {
            var walletAddress = UTF8ToString(walletPtr);
            window.CapmonSolana.linkWallet(walletAddress)
                .then(function (resultJson) {
                    SendMessage('WalletManager', 'OnLinkWalletSuccess', resultJson);
                })
                .catch(function (err) {
                    SendMessage('WalletManager', 'OnLinkWalletError', String(err.message || err));
                });
        } catch (e) {
            SendMessage('WalletManager', 'OnLinkWalletError', String(e));
        }
    },

    RefreshStakeStateJS: function () {
        try {
            window.CapmonSolana.refreshStakeState()
                .then(function (resultJson) {
                    SendMessage('WalletManager', 'OnLinkWalletSuccess', resultJson);
                })
                .catch(function (err) {
                    SendMessage('WalletManager', 'OnLinkWalletError', String(err.message || err));
                });
        } catch (e) {
            SendMessage('WalletManager', 'OnLinkWalletError', String(e));
        }
    },
SignMessageJS: function (messagePtr) {
        try {
            var message = UTF8ToString(messagePtr);
            window.CapmonSolana.signMessage(message)
                .then(function (sigJson) {
                    SendMessage('WalletManager', 'OnSignMessageSuccess', sigJson);
                })
                .catch(function (err) {
                    SendMessage('WalletManager', 'OnSignMessageError', String(err.message || err));
                });
        } catch (e) {
            SendMessage('WalletManager', 'OnSignMessageError', String(e));
        }
    },
});