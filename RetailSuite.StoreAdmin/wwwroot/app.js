// RetailSuite StoreAdmin — client-side helpers

// ---------------------------------------------------------------------------
// Storefront header height — exposed as --shop-header-height so the mobile
// categories drawer and the desktop sticky sidebar can offset below the
// header without a hardcoded guess. The header's rendered height varies by
// device/font/text-wrap, which is what made fixed pixel offsets unreliable.
// ---------------------------------------------------------------------------
(function () {
    let observed = false;
    function updateShopHeaderHeight() {
        const header = document.querySelector('.shop-header');
        if (header) document.documentElement.style.setProperty('--shop-header-height', header.offsetHeight + 'px');
        return header;
    }
    function tryObserve() {
        const header = updateShopHeaderHeight();
        if (header && !observed && window.ResizeObserver) {
            new ResizeObserver(updateShopHeaderHeight).observe(header);
            observed = true;
        }
    }
    tryObserve();
    document.addEventListener('DOMContentLoaded', tryObserve);
    window.addEventListener('load', tryObserve);
    window.addEventListener('resize', updateShopHeaderHeight);
    // Blazor Server hydrates after the initial paint — a couple of delayed
    // retries catch the header if it wasn't in the DOM yet on first attempt.
    setTimeout(tryObserve, 300);
    setTimeout(tryObserve, 1000);
})();

// ---------------------------------------------------------------------------
// Barcode scanner support (POS page)
// Keep focus on the hidden barcode input so physical scanners work
// ---------------------------------------------------------------------------
window.focusBarcodeInput = function () {
    const el = document.getElementById('barcodeInput');
    if (el) el.focus();
};

// ---------------------------------------------------------------------------
// Receipt printer — opens a minimal print window
// ---------------------------------------------------------------------------
window.printReceipt = function () {
    const el = document.getElementById('receipt-printable');
    if (!el) { console.warn('receipt-printable element not found'); return; }

    const content = el.innerHTML;
    const w = window.open('', '_blank', 'width=400,height=600');
    if (!w) { alert('Pop-up blocked — please allow pop-ups and try again.'); return; }

    w.document.write(`
<!DOCTYPE html>
<html>
<head>
  <title>Receipt</title>
  <style>
    body { font-family: 'Courier New', monospace; width: 320px; margin: 16px auto; font-size: 13px; }
    h2, h3 { text-align: center; margin: 4px 0; }
    hr { border: none; border-top: 1px dashed #999; margin: 8px 0; }
    table { width: 100%; border-collapse: collapse; }
    td { padding: 2px 4px; }
    .right { text-align: right; }
    .total { font-weight: bold; font-size: 15px; }
  </style>
</head>
<body>${content}</body>
</html>`);

    w.document.close();
    w.focus();
    w.print();
    // Delay close to allow print dialog to appear
    setTimeout(() => w.close(), 2000);
};

// ---------------------------------------------------------------------------
// CSV download helper — used by Reports pages to export tables
// ---------------------------------------------------------------------------
window.downloadCsv = function (filename, csv) {
    if (!csv) return;
    // BOM so Excel opens with UTF-8 correctly
    const blob = new Blob(["﻿" + csv], { type: 'text/csv;charset=utf-8;' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = filename || 'export.csv';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 500);
};
