// Inject navbar, footer, and analytics
async function loadFragments() {
    try {
        // Load configuration
        if (typeof KAFKALENS_CONFIG === 'undefined') {
            const configScript = document.createElement('script');
            configScript.src = '/assets/config.js';
            document.head.appendChild(configScript);
            await new Promise(resolve => configScript.onload = resolve);
        }

        // Load analytics logic
        if (typeof Analytics === 'undefined') {
            const analyticsLogicScript = document.createElement('script');
            analyticsLogicScript.src = '/assets/analytics.js';
            document.head.appendChild(analyticsLogicScript);
            await new Promise(resolve => analyticsLogicScript.onload = resolve);
        }

        // Inject Umami analytics script directly to head
        const analyticsScript = document.createElement('script');
        analyticsScript.defer = true;
        analyticsScript.src = KAFKALENS_CONFIG.umami.scriptUrl;
        analyticsScript.setAttribute('data-website-id', KAFKALENS_CONFIG.umami.websiteId);
        document.head.appendChild(analyticsScript);

        // Load navbar
        const navbarResponse = await fetch('navbar.html');
        const navbarHtml = await navbarResponse.text();
        const navbarPlaceholder = document.getElementById('navbar-placeholder');
        if (navbarPlaceholder) {
            navbarPlaceholder.innerHTML = navbarHtml;
            
            // Set active class on current page
            const currentPage = window.location.pathname.split('/').pop() || 'index.html';
            const activeLink = document.querySelector(`.nav-link[data-page="${currentPage.replace('.html', '')}"]`);
            if (activeLink) {
                activeLink.classList.add('active');
            }
        }

        // Load footer
        const footerResponse = await fetch('footer.html');
        const footerHtml = await footerResponse.text();
        const footerPlaceholder = document.getElementById('footer-placeholder');
        if (footerPlaceholder) {
            footerPlaceholder.innerHTML = footerHtml;
        }

        // Reinitialize mobile menu toggle after fragments are loaded
        initMobileMenu();
    } catch (error) {
        console.error('Error loading fragments:', error);
    }
}

function initMobileMenu() {
    const navToggle = document.querySelector('.nav-toggle');
    const navLinks = document.querySelector('.nav-links');
    
    if (navToggle && navLinks) {
        navToggle.addEventListener('click', () => {
            navLinks.classList.toggle('active');
            navToggle.classList.toggle('active');
        });
    }
}

// Load fragments when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', loadFragments);
} else {
    loadFragments();
}
