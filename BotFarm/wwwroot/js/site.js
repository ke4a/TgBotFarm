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
