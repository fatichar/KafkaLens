(function() {
    // UTM Parameters Handling
    const UTM_PARAMS = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_term', 'utm_content'];

    function captureUtmParams() {
        const urlParams = new URLSearchParams(window.location.search);
        let hasUtm = false;
        const storedUtm = {};

        UTM_PARAMS.forEach(param => {
            if (urlParams.has(param)) {
                storedUtm[param] = urlParams.get(param);
                hasUtm = true;
            }
        });

        if (hasUtm) {
            localStorage.setItem('kafkale_utm', JSON.stringify(storedUtm));
        }
    }

    function getStoredUtmParams() {
        const stored = localStorage.getItem('kafkale_utm');
        return stored ? JSON.parse(stored) : {};
    }

    function mergeParamsIntoUrl(targetUrl, sourceUrl = window.location.href) {
        const targetObj = new URL(targetUrl, window.location.origin);
        const sourceParams = new URL(sourceUrl).searchParams;
        const storedUtm = getStoredUtmParams();

        // 1. Apply stored UTM parameters
        for (const [key, value] of Object.entries(storedUtm)) {
            targetObj.searchParams.set(key, value);
        }

        // 2. Apply/Overwrite with active query parameters from current page
        for (const [key, value] of sourceParams.entries()) {
            targetObj.searchParams.set(key, value);
        }

        return targetObj.toString();
    }

    // Event Tracking
    function trackEvent(name, data = {}) {
        return new Promise((resolve) => {
            if (window.umami && typeof window.umami.track === 'function') {
                const utm = getStoredUtmParams();
                const eventData = { ...utm, ...data };
                window.umami.track(name, eventData);
                // Give it a small buffer to ensure the request is sent
                setTimeout(resolve, 300);
            } else {
                console.warn(`Umami not loaded. Event ${name} not tracked.`);
                resolve();
            }
        });
    }

    // Global Click Interceptor
    document.addEventListener('click', async function(e) {
        const target = e.target.closest('a');
        if (!target) return;

        const href = target.getAttribute('href');
        if (!href) return;

        // Internal Download Links
        const osMatch = href.match(/\/download\/(windows|macos|linux)/);
        if (osMatch) {
            e.preventDefault();
            const os = osMatch[1];
            const downloadUrl = getDownloadUrl(os);

            if (downloadUrl) {
                await trackEvent(`download_${os}`);
                window.location.href = mergeParamsIntoUrl(downloadUrl);
            }
            return;
        }

        // GitHub Links
        if (href.includes('github.com')) {
            e.preventDefault();
            const isRelease = href.includes('/releases');
            await trackEvent(isRelease ? 'view_release_notes' : 'view_github');
            window.open(href, target.getAttribute('target') || '_self');
            return;
        }

        // Buy Me A Coffee
        if (href.includes('buymeacoffee.com')) {
            e.preventDefault();
            await trackEvent('buy_me_a_coffee');
            window.open(href, target.getAttribute('target') || '_self');
            return;
        }
    });

    // Initialize
    captureUtmParams();

    // Export to window
    window.Analytics = {
        trackEvent,
        getStoredUtmParams,
        mergeParamsIntoUrl
    };
})();
