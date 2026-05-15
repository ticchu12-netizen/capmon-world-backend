mergeInto(LibraryManager.library, {

    /**
     * Auto-resume Unity WebGL when the user returns to the game window after
     * an external popup (X OAuth, Phantom wallet, etc.).
     *
     * Why this is safe (and Grok's "blur listener" version isn't):
     * - We only act on WINDOW FOCUS RETURNING (user came back), never on BLUR
     *   (user is opening a popup — that focus belongs to them).
     * - We don't try to keep the game running while focus is elsewhere. The
     *   browser controls that and will pause/throttle regardless. Fighting it
     *   with setTimeout focus loops just breaks OAuth popups.
     * - We just nudge the canvas to refocus once the user is back, so they
     *   don't have to manually click into the game area to wake it.
     *
     * Call this ONCE at game startup (e.g. GameManager.Start()).
     */
    EnableWebGLAutoResume: function () {
        if (window.__capmonAutoResumeInstalled) {
            console.log('[WebGL] Auto-resume already installed, skipping');
            return;
        }
        window.__capmonAutoResumeInstalled = true;

        function findCanvas() {
            return document.getElementById('unity-canvas') ||
                   document.querySelector('canvas');
        }

        function refocusCanvasSoon() {
            // Defer slightly so the popup teardown / focus return finishes first
            setTimeout(function () {
                var c = findCanvas();
                if (c && document.activeElement !== c) {
                    try { c.focus({ preventScroll: true }); } catch (e) { c.focus(); }
                }
            }, 150);
        }

        // Primary trigger: user returns to the game tab/window
        window.addEventListener('focus', refocusCanvasSoon);

        // Backup trigger: tab becomes visible again (handles tab-switch case)
        document.addEventListener('visibilitychange', function () {
            if (!document.hidden) refocusCanvasSoon();
        });

        // Initial focus so first interaction works without manual click
        refocusCanvasSoon();

        console.log('[WebGL] Auto-resume on window focus enabled');
    }

});
