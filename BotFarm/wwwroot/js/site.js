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

window.setDarkModePreferenceCookie = async function (value) {
    if (value !== 'true' && value !== 'false') {
        throw new Error('Invalid dark mode preference.');
    }

    await window.cookieStore.set({
        name: 'darkModePreference',
        value,
        maxAge: 31536000,
        path: '/',
        sameSite: 'lax',
        secure: window.location.protocol === 'https:'
    });
}
