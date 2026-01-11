async function showToast(text, success) {
    let toastContainer = document.getElementById('toast-container');

    let toastEl = document.createElement('div');
    toastEl.classList.add('toast', 'text-white');
    toastEl.setAttribute('role', 'alert');
    toastEl.setAttribute('aria-live', 'assertive');
    toastEl.setAttribute('aria-atomic', 'true');

    if (success) {
        toastEl.classList.add('bg-success', 'bg-gradient');
    }
    else {
        toastEl.classList.add('bg-danger', 'bg-gradient');
    }

    let toastBody = document.createElement('div');
    toastBody.classList.add('toast-body');
    toastBody.innerText = text;

    toastEl.appendChild(toastBody);
    toastContainer.appendChild(toastEl);

    let bootstrapToast = new bootstrap.Toast(toastEl);
    bootstrapToast.show();
}

async function downloadFileFromStream (fileName, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer]);
    const url = URL.createObjectURL(blob);
    const anchorElement = document.createElement('a');
    anchorElement.href = url;
    anchorElement.download = fileName ?? '';
    anchorElement.click();
    anchorElement.remove();
    URL.revokeObjectURL(url);
}

window.initializeQuillEditor = function () {
    const editorEl = document.getElementById('quill-editor');
    
    if (!editorEl || typeof Quill === 'undefined') {
        return;
    }

    let quill = editorEl.__quillInstance;
    if (!quill) {
        quill = new Quill(editorEl, {
            modules: {
                toolbar: {
                    container: '#toolbar-container',
                }
            },
            theme: 'snow'
        });
        let Parchment = Quill.import("parchment");

        let spoilerClass = new Parchment.Attributor('spoiler', 'class', {
            scope: Parchment.Scope.INLINE
        });
        Quill.register(spoilerClass, true);
        var spoilerButton = document.querySelector('#spoiler-button');
        spoilerButton.addEventListener('click', function () {
            var format = quill.getFormat();
            if (format.custom) {
                quill.format('spoiler', '');
            } else {
                quill.format('spoiler', 'tg-spoiler');
            }
        });

        editorEl.__quillInstance = quill;
    } else {
        quill.setContents([]);
    }

    setTimeout(() => quill.focus(), 100);
}

window.getQuillContent = function () {
    const editorEl = document.getElementById('quill-editor');
    
    if (!editorEl) {
        return null;
    }

    const quill = editorEl.__quillInstance;
    if (!quill) {
        return null;
    }

    const text = quill.getText().trim();
    if (!text) {
        return null;
    }

    const html = quill.getSemanticHTML(0)
                      .replaceAll(/<p[^>]*>/g, '')
                      .replaceAll(/<\/p[^>]*>/g, '\n');
    
    return html;
}
