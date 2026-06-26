/**
 * Landing page — scroll reveal, stat counters, smooth interactions
 */
(function () {
    'use strict';

    function initScrollReveal() {
        var els = document.querySelectorAll('.mai-reveal, .feature-card, .stat-card, .step-item, .capability-card, .specialty-card, .testimonial-card, .tech-logo-item');
        if (!els.length) return;

        els.forEach(function (el) {
            if (!el.classList.contains('mai-reveal')) el.classList.add('mai-reveal');
        });

        if (!('IntersectionObserver' in window)) {
            els.forEach(function (el) { el.classList.add('mai-revealed'); });
            return;
        }

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('mai-revealed');
                    observer.unobserve(entry.target);
                }
            });
        }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });

        els.forEach(function (el) { observer.observe(el); });
    }

    function animateCounter(el) {
        var target = parseInt(el.getAttribute('data-count') || '0', 10);
        var suffix = el.getAttribute('data-suffix') || '';
        var prefix = el.getAttribute('data-prefix') || '';
        var duration = 1800;
        var start = 0;
        var startTime = null;

        function step(ts) {
            if (!startTime) startTime = ts;
            var progress = Math.min((ts - startTime) / duration, 1);
            var eased = 1 - Math.pow(1 - progress, 3);
            var current = Math.floor(start + (target - start) * eased);
            el.textContent = prefix + current.toLocaleString() + suffix;
            if (progress < 1) requestAnimationFrame(step);
        }

        requestAnimationFrame(step);
    }

    function initCounters() {
        var counters = document.querySelectorAll('[data-count]');
        if (!counters.length) return;

        if (!('IntersectionObserver' in window)) {
            counters.forEach(animateCounter);
            return;
        }

        var obs = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    animateCounter(entry.target);
                    obs.unobserve(entry.target);
                }
            });
        }, { threshold: 0.5 });

        counters.forEach(function (c) { obs.observe(c); });
    }

    function initDemoButton() {
        document.querySelectorAll('a.btn-hero-demo[href^="#"]').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                var id = btn.getAttribute('href');
                if (!id || id === '#') return;
                var target = document.querySelector(id);
                if (target) {
                    e.preventDefault();
                    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
            });
        });
    }

    function initNavScroll() {
        var navs = document.querySelectorAll('.nav-custom');
        window.addEventListener('scroll', function () {
            var scrolled = window.scrollY > 20;
            navs.forEach(function (nav) {
                nav.classList.toggle('nav-scrolled', scrolled);
            });
        }, { passive: true });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initScrollReveal();
        initCounters();
        initDemoButton();
        initNavScroll();
    });
})();
