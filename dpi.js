window.getDpi = function () {
    const dpi = window.devicePixelRatio * 96; // 96 DPI is the standard DPI for most screens
    return dpi;
};
