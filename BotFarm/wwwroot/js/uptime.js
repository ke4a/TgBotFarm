// Live uptime display: the server sends its uptime once and the browser counts up from there,
// so a ticking clock costs no server renders or circuit traffic.

/** Formats seconds as dd.hh:mm:ss, the same format the AppStats health check reports. */
export function formatUptime(totalSeconds) {
    const t = Math.max(0, Math.floor(totalSeconds));
    const pad = n => String(n).padStart(2, '0');
    return `${pad(Math.floor(t / 86400))}.${pad(Math.floor(t / 3600) % 24)}:${pad(Math.floor(t / 60) % 60)}:${pad(t % 60)}`;
}

/**
 * Shows the uptime in element and updates it every second, counting from seconds at call time.
 * Writes into the element's existing text node when it has one, so a Blazor-rendered value can
 * serve as the fallback and Blazor can still update that same node.
 */
export function startUptime(element, seconds) {
    const started = performance.now();
    const update = () => {
        const text = formatUptime(seconds + (performance.now() - started) / 1000);
        const node = element.firstChild;
        if (node && node.nodeType === Node.TEXT_NODE && node === element.lastChild) {
            node.nodeValue = text;
        } else {
            element.textContent = text;
        }
    };
    update();
    const timer = setInterval(update, 1000);
    return { dispose: () => clearInterval(timer) };
}
