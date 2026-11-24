/* Pressing Éclat — JavaScript minimal (vanilla, sans dépendance). */
(function () {
    'use strict';

    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    /* ---- En-tête : devient opaque au défilement ---- */
    var header = document.getElementById('site-header');
    function onScroll() {
        if (header) header.classList.toggle('is-scrolled', window.scrollY > 12);
    }
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });

    /* ---- Menu mobile accessible ---- */
    var toggle = document.querySelector('[data-menu-toggle]');
    var menu = document.getElementById('mobile-menu');
    function setMenu(open) {
        if (!toggle || !menu) return;
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        toggle.querySelector('.visually-hidden').textContent = open ? 'Fermer le menu' : 'Ouvrir le menu';
        menu.hidden = !open;
        if (header) header.classList.toggle('is-scrolled', open || window.scrollY > 12);
    }
    if (toggle && menu) {
        toggle.addEventListener('click', function () {
            setMenu(toggle.getAttribute('aria-expanded') !== 'true');
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && toggle.getAttribute('aria-expanded') === 'true') {
                setMenu(false);
                toggle.focus();
            }
        });
        window.matchMedia('(min-width: 992px)').addEventListener('change', function (e) {
            if (e.matches) setMenu(false);
        });
    }

    /* ---- Révélation au défilement ---- */
    var targets = [].slice.call(document.querySelectorAll('[data-reveal]'));
    if (targets.length && 'IntersectionObserver' in window && !reduceMotion) {
        var vh = window.innerHeight;
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('is-in');
                    io.unobserve(entry.target);
                }
            });
        }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
        targets.forEach(function (el, i) {
            // Ce qui est déjà visible au chargement n'est jamais masqué (pas de flash).
            if (el.getBoundingClientRect().top < vh * 0.92) return;
            el.style.setProperty('--d', (i % 3) * 80 + 'ms');
            el.classList.add('reveal-init');
            io.observe(el);
        });
    }

    /* ---- Tarifs : recherche + filtre par service ---- */
    var filter = document.querySelector('[data-tarif-filter]');
    if (filter) {
        var search = filter.querySelector('[data-search]');
        var chips = [].slice.call(filter.querySelectorAll('[data-chip]'));
        var cards = [].slice.call(document.querySelectorAll('[data-tarif-card]'));
        var empty = document.querySelector('[data-tarif-empty]');
        var service = 'all';

        var normalize = function (s) {
            return (s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').trim();
        };

        var apply = function () {
            var q = normalize(search ? search.value : '');
            var visible = 0;
            cards.forEach(function (card) {
                var nameOk = !q || normalize(card.getAttribute('data-name')).indexOf(q) !== -1;
                var lines = [].slice.call(card.querySelectorAll('[data-service]'));
                var shown = 0;
                lines.forEach(function (line) {
                    var ok = service === 'all' || line.getAttribute('data-service') === service;
                    line.hidden = !ok;
                    if (ok) shown++;
                });
                var show = nameOk && shown > 0;
                card.hidden = !show;
                if (show) visible++;
            });
            if (empty) empty.hidden = visible !== 0;
        };

        if (search) search.addEventListener('input', apply);
        chips.forEach(function (chip) {
            chip.addEventListener('click', function () {
                service = chip.getAttribute('data-chip');
                chips.forEach(function (c) {
                    var on = c === chip;
                    c.classList.toggle('is-active', on);
                    c.setAttribute('aria-pressed', on ? 'true' : 'false');
                });
                apply();
            });
        });
    }

    /* ---- Afficher / masquer le mot de passe ---- */
    [].slice.call(document.querySelectorAll('[data-toggle-password]')).forEach(function (btn) {
        btn.addEventListener('click', function () {
            var input = document.getElementById(btn.getAttribute('data-toggle-password'));
            if (!input) return;
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.setAttribute('aria-label', show ? 'Masquer le mot de passe' : 'Afficher le mot de passe');
            btn.style.color = show ? 'var(--brass-text)' : '';
        });
    });

    /* ---- Évite les doubles envois de formulaire ---- */
    [].slice.call(document.querySelectorAll('form[method="post"]')).forEach(function (form) {
        form.addEventListener('submit', function () {
            var btn = form.querySelector('button[type="submit"]');
            if (btn && !btn.classList.contains('is-loading')) {
                setTimeout(function () { btn.classList.add('is-loading'); }, 0);
            }
        });
    });

    /* ---- Page « service indisponible » : bouton réessayer ---- */
    [].slice.call(document.querySelectorAll('[data-reload]')).forEach(function (btn) {
        btn.addEventListener('click', function () { window.location.reload(); });
    });

    /* ---- Messages d'erreur : met le focus pour les lecteurs d'écran ---- */
    var focusTarget = document.querySelector('[data-focus-on-load]');
    if (focusTarget) focusTarget.focus({ preventScroll: false });

    /* ---- Messages de confirmation : disparition douce après quelques secondes ---- */
    [].slice.call(document.querySelectorAll('.flash-zone .flash-success')).forEach(function (el) {
        setTimeout(function () {
            el.style.transition = 'opacity 500ms, transform 500ms';
            el.style.opacity = '0';
            el.style.transform = 'translateY(-6px)';
            setTimeout(function () { el.remove(); }, 520);
        }, 7000);
    });
})();
