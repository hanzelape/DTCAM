// DTCAM — progressive enhancement layer.
// Everything here is optional: the app is fully usable if this file
// fails to load, so each feature guards its own prerequisites.

(function () {
  'use strict';

  var reduceMotion = window.matchMedia
    ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
    : false;

  /* ---------------------------------------------------------
     Count-up for statistic tiles.
     Reads the server-rendered value out of the text, then animates
     from zero to it. If the value is not numeric we leave the DOM
     untouched so nothing is lost.
     --------------------------------------------------------- */
  function countUp(el) {
    var raw = (el.textContent || '').trim();
    var match = raw.match(/(-?[\d,]+(?:\.\d+)?)/);
    if (!match) return;

    var target = parseFloat(match[1].replace(/,/g, ''));
    if (isNaN(target)) return;

    var decimals = (match[1].split('.')[1] || '').length;
    var prefix = raw.slice(0, match.index);
    var suffix = raw.slice(match.index + match[1].length);

    function format(n) {
      var fixed = n.toFixed(decimals);
      var parts = fixed.split('.');
      parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',');
      return prefix + parts.join('.') + suffix;
    }

    if (reduceMotion) {
      el.textContent = format(target);
      return;
    }

    var duration = 900;
    var start = null;

    function step(ts) {
      if (start === null) start = ts;
      var progress = Math.min((ts - start) / duration, 1);
      // easeOutExpo — fast start, gentle settle, so it lands cleanly.
      var eased = progress === 1 ? 1 : 1 - Math.pow(2, -10 * progress);
      el.textContent = format(target * eased);
      if (progress < 1) requestAnimationFrame(step);
      else el.textContent = format(target);
    }

    requestAnimationFrame(step);
  }

  /* ---------------------------------------------------------
     Reveal-on-scroll.
     Elements marked .reveal start hidden and are released as they
     enter the viewport. If IntersectionObserver is missing we reveal
     everything immediately rather than leaving the page blank.
     --------------------------------------------------------- */
  function initReveal() {
    var targets = document.querySelectorAll('.reveal');
    if (!targets.length) return;

    if (!('IntersectionObserver' in window) || reduceMotion) {
      targets.forEach(function (el) { el.classList.add('is-revealed'); });
      return;
    }

    targets.forEach(function (el) { el.classList.add('reveal-ready'); });

    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('is-revealed');
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });

    targets.forEach(function (el) { observer.observe(el); });
  }

  /* ---------------------------------------------------------
     Apply .stagger to repeated groups so lists cascade in
     without needing the class in every view.
     --------------------------------------------------------- */
  function initStagger() {
    document.querySelectorAll('[data-stagger]').forEach(function (group) {
      group.classList.add('stagger');
      var mode = group.getAttribute('data-stagger');
      if (mode === 'sidebar') group.classList.add('sidebar-nav-stagger');
    });
  }

  /* ---------------------------------------------------------
     Ripple on primary buttons, positioned from the click point.
     --------------------------------------------------------- */
  function initRipple() {
    document.addEventListener('pointerdown', function (e) {
      var btn = e.target.closest ? e.target.closest('.btn-primary') : null;
      if (!btn || reduceMotion) return;

      var rect = btn.getBoundingClientRect();
      var size = Math.max(rect.width, rect.height);
      var ripple = document.createElement('span');

      ripple.className = 'btn-ripple';
      ripple.style.width = ripple.style.height = size + 'px';
      ripple.style.left = (e.clientX - rect.left - size / 2) + 'px';
      ripple.style.top = (e.clientY - rect.top - size / 2) + 'px';

      btn.appendChild(ripple);
      ripple.addEventListener('animationend', function () {
        ripple.remove();
      });
    });
  }

  /* ---------------------------------------------------------
     Entry point — waits for DOM so querySelectorAll has content.
     --------------------------------------------------------- */
  function init() {
    try { initStagger(); } catch (e) { /* non-critical */ }
    try { initReveal(); } catch (e) { /* non-critical */ }
    try { initRipple(); } catch (e) { /* non-critical */ }

    document.querySelectorAll('.stat-value').forEach(function (el) {
      countUp(el);
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
