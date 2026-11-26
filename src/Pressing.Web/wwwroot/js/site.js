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
        // Certains navigateurs plus anciens exposent seulement addListener() sur MediaQueryList.
        var desktopQuery = window.matchMedia ? window.matchMedia('(min-width: 992px)') : null;
        function onDesktopChange(e) { if (e.matches) setMenu(false); }
        if (desktopQuery) {
            if (desktopQuery.addEventListener) desktopQuery.addEventListener('change', onDesktopChange);
            else if (desktopQuery.addListener) desktopQuery.addListener(onDesktopChange);
        }
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
            var value = (s || '').toLowerCase().trim();
            return String.prototype.normalize
                ? value.normalize('NFD').replace(/[̀-ͯ]/g, '')
                : value;
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

    /* ---- Suivi : validation locale, sans requête inutile ---- */
    var trackingForm = document.querySelector('form[action="/suivi"]');
    if (trackingForm) {
        var ticketField = trackingForm.querySelector('#ticket');
        var phoneField = trackingForm.querySelector('#phone');
        var trackingError = document.getElementById('tracking-error');
        var trackingText = document.getElementById('tracking-error-text');
        var requiredMessage = trackingForm.getAttribute('data-required-message') ||
            'Veuillez indiquer un numéro de suivi et un numéro de téléphone.';
        var partialMessage = trackingForm.getAttribute('data-partial-message') ||
            'Renseignez à la fois le numéro de ticket et le téléphone utilisé lors du dépôt.';

        trackingForm.addEventListener('submit', function (e) {
            var ticketOk = ticketField && ticketField.value.trim().length > 0;
            var phoneOk = phoneField && phoneField.value.trim().length > 0;
            if (ticketOk && phoneOk) {
                if (trackingError) trackingError.hidden = true;
                return;
            }

            e.preventDefault();
            if (trackingText) trackingText.textContent = ticketOk || phoneOk ? partialMessage : requiredMessage;
            if (trackingError) trackingError.hidden = false;
            (ticketOk ? phoneField : ticketField || phoneField).focus();
        });

        [ticketField, phoneField].forEach(function (field) {
            if (field) field.addEventListener('input', function () {
                if (trackingError && !trackingError.hidden) trackingError.hidden = true;
            });
        });
    }

    /* ---- Évite les doubles envois de formulaire ---- */
    function restoreSubmitButtons(form) {
        [].slice.call(form.querySelectorAll('button[type="submit"], input[type="submit"]')).forEach(function (btn) {
            btn.disabled = btn.getAttribute('data-pressing-previous-disabled') === 'true';
            btn.removeAttribute('data-pressing-previous-disabled');
            btn.removeAttribute('aria-busy');
            if (btn.classList) btn.classList.remove('is-loading');
        });
        form.removeAttribute('data-submitting');
    }

    [].slice.call(document.querySelectorAll('form[method="post"]')).forEach(function (form) {
        form.addEventListener('submit', function (e) {
            if (form.getAttribute('data-submitting') === 'true') {
                e.preventDefault();
                return;
            }

            form.setAttribute('data-submitting', 'true');
            [].slice.call(form.querySelectorAll('button[type="submit"], input[type="submit"]')).forEach(function (btn) {
                btn.setAttribute('data-pressing-previous-disabled', btn.disabled ? 'true' : 'false');
                btn.disabled = true;
                btn.setAttribute('aria-busy', 'true');
                if (btn.classList) btn.classList.add('is-loading');
            });
        });
    });

    // Retour « précédent » / cache du navigateur : les boutons ne restent jamais désactivés.
    window.addEventListener('pageshow', function () {
        [].slice.call(document.querySelectorAll('form[data-submitting="true"]')).forEach(restoreSubmitButtons);
    });

    /* ---- Page « service indisponible » : réessayer en GET, jamais en POST ---- */
    [].slice.call(document.querySelectorAll('[data-reload]')).forEach(function (btn) {
        btn.addEventListener('click', function () {
            if (btn.disabled) return;
            btn.disabled = true;
            var url = btn.getAttribute('data-retry-url') || (window.location.pathname + window.location.search);
            window.location.assign(url);
        });
    });

    /* ---- Messages d'erreur : met le focus pour les lecteurs d'écran ---- */
    var focusTarget = document.querySelector('[data-focus-on-load]');
    if (focusTarget) {
        try { focusTarget.focus({ preventScroll: false }); }
        catch (err) { focusTarget.focus(); }
    }

    /* ---- Messages de succès : fermeture manuelle et persistants ---- */
    function closestFlash(el) {
        while (el && el !== document) {
            if (el.classList && el.classList.contains('flash')) return el;
            el = el.parentElement;
        }
        return null;
    }

    [].slice.call(document.querySelectorAll('[data-close-flash]')).forEach(function (btn) {
        btn.addEventListener('click', function () {
            var flash = closestFlash(btn);
            if (flash) flash.parentNode.removeChild(flash);
        });
    });
})();
