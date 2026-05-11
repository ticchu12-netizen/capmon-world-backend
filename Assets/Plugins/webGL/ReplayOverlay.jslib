mergeInto(LibraryManager.library, {

    /**
     * Show the replay overlay for a specific battle.
     * Lazy-creates the overlay div + iframe on first call.
     * Subsequent calls reuse the same overlay and just swap the iframe src.
     */
    ShowReplayOverlay: function(battleIdPtr) {
        var battleId = UTF8ToString(battleIdPtr);
        if (!battleId) {
            console.warn('[ReplayOverlay] Show called with empty battleId');
            return;
        }

        var overlay = document.getElementById('capmon-replay-overlay');

        if (!overlay) {
            // Create overlay container — full-screen modal positioned over Unity canvas
            overlay = document.createElement('div');
            overlay.id = 'capmon-replay-overlay';
            overlay.style.cssText = [
                'position: fixed',
                'top: 4%',
                'left: 4%',
                'width: 92%',
                'height: 92%',
                'background: rgba(10, 14, 26, 0.97)',
                'z-index: 99999',
                'border: 1px solid #ffd700',
                'border-radius: 14px',
                'box-shadow: 0 0 40px rgba(255, 215, 0, 0.35), 0 0 0 1px rgba(255, 215, 0, 0.15)',
                'overflow: hidden',
                'display: none',
                'backdrop-filter: blur(6px)'
            ].join(';');

            // Close button (top-right)
            var closeBtn = document.createElement('button');
            closeBtn.id = 'capmon-replay-close';
            closeBtn.textContent = '×';
            closeBtn.style.cssText = [
                'position: absolute',
                'top: 14px',
                'right: 18px',
                'background: rgba(230, 61, 0, 0.25)',
                'color: #fff',
                'border: 1px solid #e63d00',
                'border-radius: 50%',
                'width: 38px',
                'height: 38px',
                'font-size: 24px',
                'font-weight: 600',
                'cursor: pointer',
                'z-index: 100000',
                'line-height: 1',
                'padding: 0',
                'font-family: monospace'
            ].join(';');
            closeBtn.onclick = function() {
                overlay.style.display = 'none';
            };
            overlay.appendChild(closeBtn);

            // Iframe that loads the /replay page
            var iframe = document.createElement('iframe');
            iframe.id = 'capmon-replay-iframe';
            iframe.style.cssText = 'width:100%; height:100%; border:0; display:block;';
            iframe.allow = 'autoplay; fullscreen';
            overlay.appendChild(iframe);

            // ESC key closes the overlay
            document.addEventListener('keydown', function(e) {
                if (e.key === 'Escape' && overlay.style.display === 'block') {
                    overlay.style.display = 'none';
                }
            });

            document.body.appendChild(overlay);
        }

        var iframe = document.getElementById('capmon-replay-iframe');
        iframe.src = 'https://capmon-hackathon.web.app/replay?id=' + encodeURIComponent(battleId);
        overlay.style.display = 'block';
    },

    /**
     * Hide the overlay if it's visible. Safe to call when not yet created.
     */
    HideReplayOverlay: function() {
        var overlay = document.getElementById('capmon-replay-overlay');
        if (overlay) overlay.style.display = 'none';
    }

});
