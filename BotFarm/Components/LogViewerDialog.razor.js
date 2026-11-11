// Keeps the log viewer pinned to its newest entry while the user is at the bottom ("following"),
// and stops following as soon as they scroll up to read older entries.

const states = new WeakMap();

// How close to the bottom, in pixels, still counts as "at the bottom".
const bottomThreshold = 24;

function pin(element) {
    element.scrollTop = element.scrollHeight;
}

export function attach(element, follow) {
    if (!element || states.has(element)) {
        return;
    }

    const state = { follow: !!follow };
    state.onScroll = () => {
        state.follow = element.scrollHeight - element.scrollTop - element.clientHeight <= bottomThreshold;
    };
    // New entries and the virtualized list's spacer resizes both show up as DOM mutations.
    state.observer = new MutationObserver(() => {
        if (state.follow) {
            pin(element);
        }
    });
    state.observer.observe(element, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['style'] });
    element.addEventListener('scroll', state.onScroll, { passive: true });
    states.set(element, state);

    if (state.follow) {
        pin(element);
    }
}

export function follow(element) {
    const state = states.get(element);
    if (!state) {
        return;
    }

    state.follow = true;
    pin(element);
}

export function detach(element) {
    const state = states.get(element);
    if (!state) {
        return;
    }

    state.observer.disconnect();
    element.removeEventListener('scroll', state.onScroll);
    states.delete(element);
}
