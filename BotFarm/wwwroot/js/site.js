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

// Returns the browser's IANA time zone and keeps it in a cookie, so the server can show times in it from the first
// render of the next page load.
window.getBrowserTimeZone = async function () {
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    await window.cookieStore.set({
        name: 'timeZone',
        value: timeZone,
        maxAge: 31536000,
        path: '/',
        sameSite: 'lax',
        secure: window.location.protocol === 'https:'
    });
    return timeZone;
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
