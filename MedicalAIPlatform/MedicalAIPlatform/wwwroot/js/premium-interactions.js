/**
 * Premium UI interactions — ripple, parallax, nav polish
 * Frontend only; no routing or backend changes.
 */
(function () {
    'use strict';

    /* ─── Ripple effect on buttons ─── */
    function initRipple() {
        var selectors = '.btn-hero, .btn-nav, .upload-btn, .mai-btn, .admin-qbtn, .btn-primary-auth, .btn-auth, a.btn-hero-start';
        document.querySelectorAll(selectors).forEach(function (el) {
            if (el.classList.contains('mai-ripple-ready')) return;
            el.classList.add('mai-ripple-ready', 'mai-ripple-host');
            el.addEventListener('click', function (e) {
                var rect = el.getBoundingClientRect();
                var size = Math.max(rect.width, rect.height) * 1.6;
                var ripple = document.createElement('span');
                ripple.className = 'mai-ripple-wave';
                ripple.style.width = ripple.style.height = size + 'px';
                ripple.style.left = (e.clientX - rect.left - size / 2) + 'px';
                ripple.style.top = (e.clientY - rect.top - size / 2) + 'px';
                el.appendChild(ripple);
                ripple.addEventListener('animationend', function () { ripple.remove(); });
            });
        });
    }

    /* ─── Hero parallax (subtle) ─── */
    function initHeroParallax() {
        var hero = document.querySelector('.hero-section');
        if (!hero || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        var ticking = false;
        window.addEventListener('scroll', function () {
            if (ticking) return;
            ticking = true;
            requestAnimationFrame(function () {
                var y = window.scrollY;
                var offset = Math.min(y * 0.12, 40);
                hero.style.setProperty('--hero-parallax', offset + 'px');
                ticking = false;
            });
        }, { passive: true });
    }

    /* ─── Navbar glass on scroll (landing + app) ─── */
    function initNavGlass() {
        var navs = document.querySelectorAll('.nav-custom, .mai-navbar, .app-header-desktop');
        if (!navs.length) return;

        function update() {
            var scrolled = window.scrollY > 16;
            navs.forEach(function (nav) {
                nav.classList.toggle('nav-glass-scrolled', scrolled);
            });
        }
        update();
        window.addEventListener('scroll', update, { passive: true });
    }

    /* ─── Dropdown keyboard + animation helper ─── */
    function initNavDropdowns() {
        document.querySelectorAll('.mai-nav-dropdown, .mai-nav-dropdown-wrap').forEach(function (wrap) {
            var toggle = wrap.querySelector('.mai-nav-dropdown-toggle');
            var menu = wrap.querySelector('.mai-nav-dropdown-menu');
            if (!toggle || !menu) return;

            toggle.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var open = wrap.classList.toggle('is-open');
                toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            });

            document.addEventListener('click', function (e) {
                if (!wrap.contains(e.target)) {
                    wrap.classList.remove('is-open');
                    toggle.setAttribute('aria-expanded', 'false');
                }
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initRipple();
        initHeroParallax();
        initNavGlass();
        initNavDropdowns();
    });
})();
