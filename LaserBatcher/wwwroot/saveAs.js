// JavaScript code in a separate file or in a <script> tag

window.saveAsFile = function (filename, content) {
    var link = document.createElement('a');
    link.href = 'data:text/plain;base64,' + content;
    link.download = filename;
    link.click();
}
