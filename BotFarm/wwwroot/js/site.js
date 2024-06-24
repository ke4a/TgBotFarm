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

function formatBytes(bytes, decimals = 2) {
    if (!+bytes) return '0 Bytes'

    const k = 1024
    const dm = decimals < 0 ? 0 : decimals
    const sizes = ['Bytes', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB', 'EiB', 'ZiB', 'YiB']

    const i = Math.floor(Math.log(bytes) / Math.log(k))

    return `${parseFloat((bytes / Math.pow(k, i)).toFixed(dm))} ${sizes[i]}`
}

let modal = document.getElementById('confirmModal');
modal.addEventListener('show.bs.modal', function (event) {
    let button = event.relatedTarget;
    let title = modal.querySelector('.modal-title');
    title.textContent = button.getAttribute('data-bs-title');
    let message = modal.querySelector('.modal-body span');
    message.textContent = button.getAttribute('data-bs-message');
    let actionButton = modal.querySelector('#action');
    actionButton.textContent = button.getAttribute('data-bs-action');
    actionButton.setAttribute('onclick', button.getAttribute('data-bs-onclick'));
});