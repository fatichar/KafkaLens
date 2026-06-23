// Inject navbar, footer, and analytics
async function loadFragments() {
    try {
        // Inject Umami analytics script directly to head
        const analyticsScript = document.createElement('script');
        analyticsScript.defer = true;
        analyticsScript.src = 'https://analytics.greenfit.in/script.js';
        analyticsScript.setAttribute('data-website-id', 'b9d3274a-0dbd-46d3-b1e9-762c04e02461');
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
