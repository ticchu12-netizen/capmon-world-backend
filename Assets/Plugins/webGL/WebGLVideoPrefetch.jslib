mergeInto(LibraryManager.library, {

    /**
     * Ask the browser to download a URL into its HTTP cache. Used to pre-warm
     * the cache for videos that will be played later (e.g., wallet screen video
     * while user is still on the login screen).
     *
     * Why this works even when the target VideoPlayer's GameObject is inactive:
     * we're not touching the VideoPlayer at all. We just issue a plain fetch()
     * for the URL. When VideoPlayer later loads the same URL via an HTML5 <video>
     * element, the browser HTTP cache serves the bytes without a network round-trip.
     *
     * Limitations:
     * - Caches the file bytes only. The video still has to be decoded on Play(),
     *   which is the second source of delay. Don't expect zero delay — just less.
     * - Browser may evict cache entries under memory pressure. Generally fine for
     *   short same-session lookups.
     */
    PrefetchVideoURL: function (urlPtr) {
        var url = UTF8ToString(urlPtr);
        if (!url) return;

        try {
            fetch(url, { cache: 'force-cache', credentials: 'same-origin' })
                .then(function (resp) {
                    if (resp.ok) {
                        // Drain the response body so the browser actually caches it
                        return resp.blob().then(function () {
                            console.log('[Prefetch] Cached: ' + url);
                        });
                    } else {
                        console.warn('[Prefetch] HTTP ' + resp.status + ' for ' + url);
                    }
                })
                .catch(function (err) {
                    console.warn('[Prefetch] Failed: ' + url + ' — ' + err);
                });
        } catch (e) {
            console.warn('[Prefetch] Threw synchronously: ' + e);
        }
    }

});
