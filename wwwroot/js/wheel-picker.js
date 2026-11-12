// iPhone-style wheel picker for time selection.
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.wheel-picker').forEach(function (picker) {
        initWheelPicker(picker);
    });
});

function initWheelPicker(picker) {
    var targetSel = picker.getAttribute('data-target');
    var target = document.querySelector(targetSel);
    if (!target) return;

    var hoursCol = picker.querySelector('.wheel-hours');
    var minutesCol = picker.querySelector('.wheel-minutes');
    var ampmCol = picker.querySelector('.wheel-ampm');

    // Build hour/minute/ampm options
    var hours = [];
    for (var h = 1; h <= 12; h++) hours.push(h);
    var minutes = [];
    for (var m = 0; m < 60; m++) minutes.push(m < 10 ? '0' + m : '' + m);
    var ampm = ['AM', 'PM'];

    hours.forEach(function (h) { hoursCol.appendChild(createOption(h)); });
    minutes.forEach(function (m) { minutesCol.appendChild(createOption(m)); });
    ampm.forEach(function (a) { ampmCol.appendChild(createOption(a)); });

    // Parse current value (HH:mm:ss)
    var value = target.value || '08:00:00';
    var parts = value.split(':');
    var hour24 = parseInt(parts[0], 10);
    var minute = parts.length > 1 ? parseInt(parts[1], 10) : 0;
    var ampmVal = hour24 >= 12 ? 'PM' : 'AM';
    var hour12 = hour24 % 12 === 0 ? 12 : hour24 % 12;

    // Set initial scroll positions
    setScroll(hoursCol, hour12);
    setScroll(minutesCol, minute);
    setScroll(ampmCol, ampmVal);

    // Update hidden input on scroll (debounced)
    var pending = null;
    function scheduleUpdate() {
        if (pending) clearTimeout(pending);
        pending = setTimeout(updateValue, 150);
    }

    function updateValue() {
        var h = getScrollValue(hoursCol);
        var m = parseInt(getScrollValue(minutesCol), 10);
        var ap = getScrollValue(ampmCol);
        var h24 = (ap === 'PM' ? (h % 12) + 12 : h % 12);
        target.value = pad(h24) + ':' + pad(m) + ':00';
    }

    [hoursCol, minutesCol, ampmCol].forEach(function (col) {
        col.addEventListener('scroll', scheduleUpdate);
    });

    // Re-center after scroll
    ['scrollend', 'mouseup', 'touchend'].forEach(function (evt) {
        [hoursCol, minutesCol, ampmCol].forEach(function (col) {
            col.addEventListener(evt, function () { snapToNearest(col); });
        });
    });

    function snapToNearest(col) {
        var item = col.querySelector('.wheel-item');
        if (!item) return;
        var itemH = item.offsetHeight;
        var idx = Math.round(col.scrollTop / itemH);
        var maxIdx = col.children.length - 1;
        if (idx < 0) idx = 0;
        if (idx > maxIdx) idx = maxIdx;
        setScroll(col, col.children[idx].textContent);
    }

    function setScroll(col, displayValue) {
        var item = col.querySelector('.wheel-item');
        if (!item) return;
        var itemH = item.offsetHeight;
        var idx = 0;
        for (var i = 0; i < col.children.length; i++) {
            if (col.children[i].textContent == displayValue) { idx = i; break; }
        }
        col.scrollTop = idx * itemH;
    }

    function getScrollValue(col) {
        var item = col.querySelector('.wheel-item');
        if (!item) return '0';
        var itemH = item.offsetHeight;
        var idx = Math.round(col.scrollTop / itemH);
        var maxIdx = col.children.length - 1;
        if (idx < 0) idx = 0;
        if (idx > maxIdx) idx = maxIdx;
        return col.children[idx].textContent;
    }
}

function createOption(text) {
    var div = document.createElement('div');
    div.className = 'wheel-item';
    div.textContent = text;
    return div;
}

function pad(n) {
    return n < 10 ? '0' + n : '' + n;
}
