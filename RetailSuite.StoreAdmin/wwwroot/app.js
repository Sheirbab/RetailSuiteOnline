// RetailSuite StoreAdmin — client-side helpers

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
