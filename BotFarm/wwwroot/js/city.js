// Home page scene: a low-poly isometric night street with Durov on a billboard
// and the farm's bots driving by as their emojis.
//
// World coordinates: x runs across the street (the road is -3..3), y runs along it,
// z is up. The road goes from the bottom-left of the view to the top-right.

const SVG_NS = 'http://www.w3.org/2000/svg';
const VIEW_W = 1600;
const VIEW_H = 900;
const S = 34;                 // pixels per world unit
const A = S * 0.866;          // iso x step
const B = S * 0.5;            // iso y step
const CX = 800;
const CY = 470;

const ROAD = 3;               // half the road width
const CURB = 0.14;           // sidewalk height above the road
const Y_MIN = -46;
const Y_MAX = 46;

const LANES = [
    { x: -2.25, dir: -1, speed: 7.2 },
    { x: -0.75, dir: -1, speed: 5.8 },
    { x: 0.75, dir: 1, speed: 6.2 },
    { x: 2.25, dir: 1, speed: 7.6 },
];

const DUROV = { y0: -4.7, y1: -0.5, z0: 3.0, z1: 8.0 };
// LED uptime panel on the hero tower's side (the +y face).
const CLOCK = { x0: -10.9, x1: -4.9, z0: 5.6, z1: 7.7 };
// Horizontal centre (in view units) of the clock and the billboard; narrow screens keep it in frame.
const FOCUS_X = 631;

const project = (x, y, z = 0) => [CX + (x - y) * A, CY + (x + y) * B - z * S];
const delta = (dx, dy, dz = 0) => [(dx - dy) * A, (dx + dy) * B - dz * S];
const fmt = n => Math.round(n * 10) / 10;
const points = list => list.map(([x, y, z]) => project(x, y, z).map(fmt).join(',')).join(' ');
const pathOf = list => 'M' + list.map(([x, y, z]) => project(x, y, z).map(fmt).join(' ')).join('L') + 'Z';

function random(seed) {
    return () => {
        seed |= 0;
        seed = (seed + 0x6d2b79f5) | 0;
        let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

// Reseeded on every mount, so the city looks the same on each visit.
const SEED = 20240917;
let rnd = random(SEED);
const between = (min, max) => min + rnd() * (max - min);
const pick = list => list[Math.floor(rnd() * list.length)];

// ---- Shared paint ---------------------------------------------------------

const DEFS = `
<defs>
  <linearGradient id="c-shade" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#0b0820" stop-opacity="0.55"/>
    <stop offset="0.6" stop-color="#0b0820" stop-opacity="0.1"/>
    <stop offset="1" stop-color="#0b0820" stop-opacity="0"/>
  </linearGradient>
  <linearGradient id="c-street-glow" x1="0" y1="1" x2="0" y2="0">
    <stop offset="0" stop-color="#ff9a7a" stop-opacity="0.45"/>
    <stop offset="0.35" stop-color="#ff7aa0" stop-opacity="0.12"/>
    <stop offset="1" stop-color="#ff7aa0" stop-opacity="0"/>
  </linearGradient>
  <radialGradient id="c-pool">
    <stop offset="0" stop-color="#ffc79a" stop-opacity="0.38"/>
    <stop offset="0.55" stop-color="#ff9a8a" stop-opacity="0.12"/>
    <stop offset="1" stop-color="#ff9a8a" stop-opacity="0"/>
  </radialGradient>
  <radialGradient id="c-lamp">
    <stop offset="0" stop-color="#fff2dc" stop-opacity="0.95"/>
    <stop offset="0.25" stop-color="#ffc58f" stop-opacity="0.45"/>
    <stop offset="1" stop-color="#ff8f8f" stop-opacity="0"/>
  </radialGradient>
  <radialGradient id="c-beacon">
    <stop offset="0" stop-color="#ff5a6e" stop-opacity="1"/>
    <stop offset="1" stop-color="#ff5a6e" stop-opacity="0"/>
  </radialGradient>
  <radialGradient id="c-tail">
    <stop offset="0" stop-color="#ff3d5e" stop-opacity="0.9"/>
    <stop offset="1" stop-color="#ff3d5e" stop-opacity="0"/>
  </radialGradient>
  <linearGradient id="c-beam-down" x1="1" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#fff4d6" stop-opacity="0.55"/>
    <stop offset="1" stop-color="#fff4d6" stop-opacity="0"/>
  </linearGradient>
  <linearGradient id="c-beam-up" x1="0" y1="1" x2="1" y2="0">
    <stop offset="0" stop-color="#fff4d6" stop-opacity="0.55"/>
    <stop offset="1" stop-color="#fff4d6" stop-opacity="0"/>
  </linearGradient>
  <radialGradient id="c-under-pink"><stop offset="0" stop-color="#ff5fb0" stop-opacity="0.75"/><stop offset="1" stop-color="#ff5fb0" stop-opacity="0"/></radialGradient>
  <radialGradient id="c-under-cyan"><stop offset="0" stop-color="#5fe8ff" stop-opacity="0.75"/><stop offset="1" stop-color="#5fe8ff" stop-opacity="0"/></radialGradient>
  <radialGradient id="c-under-violet"><stop offset="0" stop-color="#a98bff" stop-opacity="0.75"/><stop offset="1" stop-color="#a98bff" stop-opacity="0"/></radialGradient>
  <radialGradient id="c-vignette" cx="0.5" cy="0.5" r="0.75">
    <stop offset="0.55" stop-color="#07051a" stop-opacity="0"/>
    <stop offset="1" stop-color="#07051a" stop-opacity="0.6"/>
  </radialGradient>
  <linearGradient id="c-haze" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#2a1a4a" stop-opacity="0.65"/>
    <stop offset="0.35" stop-color="#2a1a4a" stop-opacity="0"/>
  </linearGradient>
  <filter id="c-neon" x="-50%" y="-50%" width="200%" height="200%">
    <feGaussianBlur stdDeviation="3.5" result="blur"/>
    <feMerge><feMergeNode in="blur"/><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
  </filter>
</defs>`;

const WINDOW_COLORS = ['#ffd59a', '#ffd59a', '#ffc27a', '#ffb3c7', '#ff9fb8', '#b9e3ff'];

// ---- Buildings ------------------------------------------------------------

function segments(from, to, minLen, maxLen, gap) {
    const result = [];
    let y = from;
    while (y < to) {
        const len = between(minLen, maxLen);
        result.push([y, Math.min(y + len, to)]);
        y += len + gap;
    }
    return result;
}

function makeCity() {
    const buildings = [];
    const far = [
        { x0: -10.2, x1: -4.2, h: [5, 11] },
        { x0: -17.6, x1: -11.4, h: [8, 15] },
        { x0: -25, x1: -18.8, h: [10, 18] },
        { x0: -33, x1: -26.2, h: [12, 20] },
    ];
    const near = [
        { x0: 5.6, x1: 9.2, h: [1.4, 2.6] },
        { x0: 10, x1: 16, h: [3, 7] },
        { x0: 17.2, x1: 23.4, h: [5, 10] },
        { x0: 24.6, x1: 31, h: [6, 12] },
        { x0: 32.2, x1: 38, h: [7, 13] },
    ];

    far.forEach((row, i) => {
        const segs = i === 0
            ? [...segments(Y_MIN, -7.8, 4.5, 7.5, 1.3), [-6.5, 1.5], ...segments(2.8, Y_MAX, 4.5, 7.5, 1.3)]
            : segments(Y_MIN + i * 1.7, Y_MAX, 5, 9, 1.3);
        for (const [y0, y1] of segs) {
            const hero = i === 0 && y0 === -6.5;
            // The building next to the hero stays low so the uptime clock on the hero's side is visible.
            const besideHero = i === 0 && y0 === 2.8;
            buildings.push({
                x0: hero ? -11.4 : row.x0 + between(0, 0.8),
                x1: row.x1,
                y0, y1,
                h: hero ? 12.5 : besideHero ? 3.6 : between(...row.h),
                side: 'far',
                depth: i,
                hero,
                shops: i === 0 && !hero,
            });
        }
    });

    near.forEach((row, i) => {
        for (const [y0, y1] of segments(Y_MIN + i * 2.3, Y_MAX, 4, 8, 1.3)) {
            buildings.push({
                x0: row.x0,
                x1: row.x1 - between(0, 0.8),
                y0, y1,
                h: between(...row.h),
                side: 'near',
                depth: i,
            });
        }
    });

    const hero = buildings.find(b => b.hero);
    hero.keepOut = [
        { along: 'y', a0: DUROV.y0, a1: DUROV.y1, z0: DUROV.z0, z1: DUROV.z1 + 1.25 },
        { along: 'x', a0: CLOCK.x0, a1: CLOCK.x1, z0: CLOCK.z0, z1: CLOCK.z1 },
    ];

    for (const b of buildings) {
        b.style = b.hero ? 'bands' : rnd() < 0.4 ? 'bands' : 'grid';
        b.hue = b.side === 'far' ? between(332, 352) : between(268, 300);
        b.roof = !b.hero && b.h > 7 && rnd() < 0.55
            ? { inset: between(0.8, 1.6), h: between(1.2, 2.8) }
            : null;
        b.beacon = b.h > 12 && rnd() < 0.5;
    }
    return buildings;
}

function faceColors(b) {
    const fade = Math.min(b.depth / 4, 1);
    if (b.side === 'far') {
        return {
            lit: `hsl(${b.hue} ${48 - fade * 18}% ${44 - fade * 22}%)`,
            side: `hsl(258 ${32 - fade * 8}% ${17 - fade * 5}%)`,
            top: `hsl(262 26% ${26 - fade * 9}%)`,
        };
    }
    return {
        lit: `hsl(${b.hue} 28% ${23 - fade * 6}%)`,
        side: `hsl(250 30% ${15 - fade * 4}%)`,
        top: `hsl(262 24% ${24 - fade * 6}%)`,
    };
}

/** Collects window quads per color so each color is a single path. */
class Windows {
    constructor() {
        this.paths = new Map();
        this.flickers = [];
    }

    add(color, quad, flicker = false) {
        if (flicker) {
            this.flickers.push({ color, d: pathOf(quad) });
            return;
        }
        this.paths.set(color, (this.paths.get(color) ?? '') + pathOf(quad));
    }

    toSvg() {
        let out = '';
        for (const [color, d] of this.paths) {
            out += `<path d="${d}" fill="${color}"${color.startsWith('#1') ? '' : ' opacity="0.92"'}/>`;
        }
        for (const f of this.flickers) {
            const style = `--d:${fmt(between(9, 26))}s;--delay:-${fmt(between(0, 26))}s`;
            out += `<path class="flicker" style="${style}" d="${f.d}" fill="${f.color}"/>`;
        }
        return out;
    }
}

// A quad on a face. "along" is the horizontal axis of the face ('x' or 'y'), "at" its fixed coordinate.
function faceQuad(along, at, a0, a1, z0, z1) {
    return along === 'y'
        ? [[at, a0, z0], [at, a1, z0], [at, a1, z1], [at, a0, z1]]
        : [[a0, at, z0], [a1, at, z0], [a1, at, z1], [a0, at, z1]];
}

function addWindows(win, b, along, at, from, to, zTop, litBias) {
    const dark = b.depth > 1 ? '#120e24' : '#1a1430';
    const lit = () => rnd() < litBias;
    const keepOut = (b.keepOut ?? []).filter(k => k.along === along);
    const avoid = (a0, a1, z0, z1) => keepOut.some(k =>
        a1 > k.a0 - 0.35 && a0 < k.a1 + 0.35 && z1 > k.z0 - 0.35 && z0 < k.z1 + 0.35);
    // Shop fronts take the ground floor of the road-facing side.
    const firstFloor = b.shops && along === 'y' ? 2.1 : 0.9;

    if (b.style === 'grid') {
        const cols = Math.max(1, Math.floor((to - from - 0.4) / 0.8));
        const step = (to - from) / cols;
        for (let z = firstFloor; z + 0.5 < zTop; z += 0.85) {
            for (let c = 0; c < cols; c++) {
                const a0 = from + c * step + step * 0.25;
                const a1 = a0 + step * 0.5;
                if (avoid(a0, a1, z, z + 0.45)) continue;
                const on = lit();
                win.add(on ? pick(WINDOW_COLORS) : dark, faceQuad(along, at, a0, a1, z, z + 0.45), on && rnd() < 0.03);
            }
        }
        return;
    }

    for (let z = firstFloor - 0.1; z + 0.4 < zTop; z += 0.7) {
        let a = from + 0.15;
        while (a < to - 0.3) {
            const len = Math.min(between(0.6, 2.4), to - 0.15 - a);
            if (!avoid(a, a + len, z, z + 0.32)) {
                const on = rnd() < litBias * 0.9;
                win.add(on ? pick(WINDOW_COLORS) : dark, faceQuad(along, at, a, a + len, z, z + 0.32), on && rnd() < 0.03);
            }
            a += len + 0.12;
        }
    }
}

function drawBox(x0, x1, y0, y1, zBase, h, colors, roadFacing) {
    const z1 = zBase + h;
    const top = [[x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]];
    const faceX = [[x1, y0, zBase], [x1, y1, zBase], [x1, y1, z1], [x1, y0, z1]];
    const faceY = [[x0, y1, zBase], [x1, y1, zBase], [x1, y1, z1], [x0, y1, z1]];
    let out = `<polygon points="${points(faceX)}" fill="${colors.lit}"/>`;
    out += `<polygon points="${points(faceY)}" fill="${colors.side}"/>`;
    out += `<polygon points="${points(faceX)}" fill="url(#c-shade)"/>`;
    out += `<polygon points="${points(faceY)}" fill="url(#c-shade)"/>`;
    if (roadFacing) {
        out += `<polygon points="${points(faceX)}" fill="url(#c-street-glow)"/>`;
    }
    out += `<polygon points="${points(top)}" fill="${colors.top}"/>`;
    // Rim light along the top edges.
    out += `<polyline points="${points([[x0, y1, z1], [x1, y1, z1], [x1, y0, z1]])}" fill="none" stroke="#ffb6c8" stroke-opacity="0.25" stroke-width="1"/>`;
    return out;
}

function drawBuilding(b) {
    const colors = faceColors(b);
    const roadFacing = b.side === 'far';
    let out = drawBox(b.x0, b.x1, b.y0, b.y1, 0, b.h, colors, roadFacing);

    const win = new Windows();
    const bias = b.side === 'far' ? 0.42 - b.depth * 0.06 : 0.3;
    addWindows(win, b, 'y', b.x1, b.y0, b.y1, b.h, bias);
    addWindows(win, b, 'x', b.y1, b.x0, b.x1, b.h, bias * 0.8);
    out += win.toSvg();
    if (b.shops) {
        out += drawShops(b);
    }

    if (b.roof) {
        const r = b.roof;
        out += drawBox(b.x0 + r.inset, b.x1 - r.inset, b.y0 + r.inset, b.y1 - r.inset, b.h, r.h, colors, false);
    }
    out += drawRoofProps(b);
    if (b.beacon) {
        const top = b.h + (b.roof?.h ?? 0);
        const [sx, sy] = project((b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2, top + 0.6);
        const [bx, by] = project((b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2, top);
        out += `<line x1="${fmt(bx)}" y1="${fmt(by)}" x2="${fmt(sx)}" y2="${fmt(sy)}" stroke="#3a2c58" stroke-width="2"/>`;
        out += `<circle class="beacon" style="--delay:-${fmt(between(0, 2.4))}s" cx="${fmt(sx)}" cy="${fmt(sy)}" r="9" fill="url(#c-beacon)"/>`;
    }
    if (b.hero) {
        out += drawDurov() + drawClock();
    }
    return out;
}

const SHOP_WORDS = ['RAMEN', 'CAFÉ', '24/7', 'BOOKS', 'VINYL', 'PHO', 'BAKERY', 'ARCADE', 'TEA', 'FLOWERS', 'NOODLES', 'LAUNDRY', 'KARAOKE', 'DUMPLINGS'];
const NEON_COLORS = ['#ff6fb1', '#7fe3ff', '#ffc27a', '#b99bff', '#8affc1'];
const STRING_COLORS = ['#ffd59a', '#ff9fb8', '#9fe8ff', '#ffe9a8'];
const LED_COLORS = ['#6dffb0', '#6ee7ff', '#6dffb0', '#ffd36e'];
let signCount = 0;
// Light that the shop windows throw onto the sidewalk; drawn later with the road.
let shopSpills = [];

/** Lit shop windows with neon name signs on the ground floor of a road-facing building. */
function drawShops(b) {
    const x = b.x1 + 0.02;
    let out = '';
    let y = b.y1 - 0.4;
    while (true) {
        const word = SHOP_WORDS[signCount % SHOP_WORDS.length];
        const width = Math.max(2.2, word.length * 0.4 + 0.7);
        if (y - width < b.y0 + 0.4) break;
        signCount++;
        const color = NEON_COLORS[signCount % NEON_COLORS.length];
        const w = width * S;
        out += `<g transform="${faceXMatrix(x, y, 1.75)}">`
            // Sign board and its neon lettering.
            + `<rect width="${fmt(w)}" height="20" rx="3" fill="#160f28" stroke="${color}" stroke-opacity="0.45"/>`
            + `<text class="neon-buzz" style="animation-delay:-${fmt(between(0, 9))}s" x="${fmt(w / 2)}" y="15" text-anchor="middle" font-family="Inter, sans-serif" font-weight="700" font-size="14" letter-spacing="2" fill="${color}" filter="url(#c-neon)">${word}</text>`
            // Shop window with mullions, and a soft awning edge.
            + `<rect x="4" y="27" width="${fmt(w - 8)}" height="28" fill="#ffc98f" opacity="0.85"/>`
            + `<rect x="4" y="27" width="${fmt(w - 8)}" height="28" fill="url(#c-shade)"/>`
            + `<path d="M${fmt(w / 3)} 27V55M${fmt((2 * w) / 3)} 27V55" stroke="#3a2848" stroke-width="2"/>`
            + `<rect x="0" y="23" width="${fmt(w)}" height="4" fill="${color}" opacity="0.55"/>`
            + '</g>';
        shopSpills.push({ x: x + 0.55, y: y - width / 2, ry: width / 2 });
        y -= width + between(0.4, 1.4);
    }
    return out;
}

function drawShopSpill(s) {
    const list = [];
    for (let i = 0; i < 18; i++) {
        const t = (i / 18) * Math.PI * 2;
        list.push([s.x + Math.cos(t) * 0.75, s.y + Math.sin(t) * s.ry, 0]);
    }
    return `<polygon class="screen" points="${points(list)}" fill="url(#c-pool)" opacity="0.8"/>`;
}

function drawAcUnit(x, y, z, near) {
    return drawBox(x, x + 0.7, y, y + 0.7, z, 0.45,
        { lit: `hsl(262 18% ${near ? 30 : 34}%)`, side: 'hsl(258 22% 20%)', top: 'hsl(262 16% 38%)' }, false);
}

function drawServerBox(x, y, z) {
    let out = drawBox(x, x + 0.8, y, y + 1.5, z, 0.85, { lit: '#2c2342', side: '#1f1832', top: '#3a2f55' }, false);
    for (let i = 0; i < 4; i++) {
        const [lx, ly] = project(x + 0.8, y + 1.5 - 0.3 - i * 0.3, z + 0.55);
        const style = `--d:${fmt(between(0.6, 2.2))}s;--delay:-${fmt(between(0, 2))}s`;
        out += `<circle class="led" style="${style}" cx="${fmt(lx)}" cy="${fmt(ly)}" r="1.6" fill="${pick(LED_COLORS)}" filter="url(#c-neon)"/>`;
    }
    return out;
}

function drawMast(x, y, z) {
    const h = between(1.8, 3.2);
    const [bx, by] = project(x, y, z);
    const [tx, ty] = project(x, y, z + h);
    let out = `<line x1="${fmt(bx)}" y1="${fmt(by)}" x2="${fmt(tx)}" y2="${fmt(ty)}" stroke="#5a4880" stroke-width="2"/>`;
    for (let k = 1; k <= 3; k++) {
        const zk = z + h * (k / 4);
        const half = 0.35 - k * 0.07;
        const [ax, ay] = project(x - half, y + half, zk);
        const [cx, cy] = project(x + half, y - half, zk);
        out += `<line x1="${fmt(ax)}" y1="${fmt(ay)}" x2="${fmt(cx)}" y2="${fmt(cy)}" stroke="#5a4880" stroke-width="1.5"/>`;
    }
    const style = `--d:${fmt(between(1.2, 2.6))}s;--delay:-${fmt(between(0, 2))}s`;
    out += `<circle class="led" style="${style}" cx="${fmt(tx)}" cy="${fmt(ty)}" r="2.4" fill="${pick(['#ff5a6e', '#6dffb0', '#6ee7ff'])}" filter="url(#c-neon)"/>`;
    return out;
}

function drawDish(x, y, z) {
    const [bx, by] = project(x, y, z);
    const [hx, hy] = project(x, y, z + 0.55);
    const tilt = pick([-35, -20, 25]);
    return `<line x1="${fmt(bx)}" y1="${fmt(by)}" x2="${fmt(hx)}" y2="${fmt(hy)}" stroke="#4a3a6a" stroke-width="2"/>`
        + `<g transform="translate(${fmt(hx)} ${fmt(hy)}) rotate(${tilt})">`
        + '<ellipse rx="10" ry="5" fill="#c9bfe6"/><ellipse rx="7" ry="3" fill="#8f84b3"/>'
        + '<line x1="0" y1="0" x2="0" y2="-9" stroke="#c9bfe6" stroke-width="1.2"/><circle cy="-9" r="1.4" fill="#c9bfe6"/>'
        + '</g>';
}

function drawWaterTank(x, y, z) {
    const r = 0.5;
    const rx = r * Math.SQRT2 * A;
    const ry = r * Math.SQRT2 * B;
    const legs = 0.55;
    const height = 1.1;
    const [cx, by] = project(x, y, z + legs);
    const [, ty] = project(x, y, z + legs + height);
    let out = '';
    for (const [dx, dy] of [[-0.35, -0.35], [0.35, -0.35], [-0.35, 0.35], [0.35, 0.35]]) {
        const [lx, ly] = project(x + dx, y + dy, z);
        const [ux, uy] = project(x + dx, y + dy, z + legs + 0.1);
        out += `<line x1="${fmt(lx)}" y1="${fmt(ly)}" x2="${fmt(ux)}" y2="${fmt(uy)}" stroke="#3a2c45" stroke-width="2"/>`;
    }
    const arc = yy => `M${fmt(cx - rx)} ${fmt(yy)}A${fmt(rx)} ${fmt(ry)} 0 0 0 ${fmt(cx + rx)} ${fmt(yy)}`;
    out += `<path d="M${fmt(cx - rx)} ${fmt(ty)}L${fmt(cx - rx)} ${fmt(by)}A${fmt(rx)} ${fmt(ry)} 0 0 0 ${fmt(cx + rx)} ${fmt(by)}L${fmt(cx + rx)} ${fmt(ty)}Z" fill="hsl(18 26% 30%)"/>`;
    out += `<path d="M${fmt(cx)} ${fmt(ty - ry)}L${fmt(cx + rx)} ${fmt(ty)}L${fmt(cx + rx)} ${fmt(by)}A${fmt(rx)} ${fmt(ry)} 0 0 1 ${fmt(cx)} ${fmt(by + ry)}Z" fill="#0b0820" opacity="0.3"/>`;
    out += `<path d="${arc(by - (by - ty) * 0.33)}${arc(by - (by - ty) * 0.7)}" fill="none" stroke="hsl(18 20% 20%)" stroke-width="1.5"/>`;
    out += `<ellipse cx="${fmt(cx)}" cy="${fmt(ty)}" rx="${fmt(rx)}" ry="${fmt(ry)}" fill="hsl(18 22% 36%)"/>`;
    out += `<polygon points="${fmt(cx - rx - 2)},${fmt(ty)} ${fmt(cx + rx + 2)},${fmt(ty)} ${fmt(cx)},${fmt(ty - 0.6 * S)}" fill="hsl(14 26% 26%)"/>`;
    out += `<polygon points="${fmt(cx)},${fmt(ty)} ${fmt(cx + rx + 2)},${fmt(ty)} ${fmt(cx)},${fmt(ty - 0.6 * S)}" fill="#0b0820" opacity="0.3"/>`;
    return out;
}

function drawGarden(x0, x1, y0, y1, z) {
    const pad = 0.35;
    const bed = [[x0 + pad, y0 + pad, z], [x1 - pad, y0 + pad, z], [x1 - pad, y1 - pad, z], [x0 + pad, y1 - pad, z]];
    let out = `<polygon points="${points(bed)}" fill="hsl(152 30% 21%)" stroke="hsl(150 28% 32%)" stroke-width="1.5"/>`;
    const trees = [];
    for (let i = Math.floor(between(2, 5)); i > 0; i--) {
        trees.push([between(x0 + 0.8, x1 - 0.8), between(y0 + 0.8, y1 - 0.8)]);
    }
    trees.sort((a, c) => (a[0] + a[1]) - (c[0] + c[1]));
    for (const [tx, ty] of trees) {
        out += drawTree(tx, ty, z, between(0.45, 0.65));
    }
    return out;
}

function drawStringLights(x0, x1, y0, y1, z) {
    const corner = [[x0 + 0.3, y1 - 0.3], [x1 - 0.3, y1 - 0.3], [x1 - 0.3, y0 + 0.3]];
    let line = '', dots = '';
    for (let s = 0; s < 2; s++) {
        const [ax, ay] = corner[s];
        const [bx, by] = corner[s + 1];
        const len = Math.hypot(bx - ax, by - ay);
        const n = Math.max(2, Math.round(len / 0.45));
        for (let i = 0; i <= n; i++) {
            const t = i / n;
            const sag = Math.sin(Math.PI * ((t * len) % 2) / 2) * 0.22;
            const [px, py] = project(ax + (bx - ax) * t, ay + (by - ay) * t, z + 0.7 - sag);
            line += `${i === 0 && s === 0 ? 'M' : 'L'}${fmt(px)} ${fmt(py)}`;
            dots += `<circle cx="${fmt(px)}" cy="${fmt(py + 1.5)}" r="1.8" fill="${STRING_COLORS[i % STRING_COLORS.length]}"/>`;
        }
    }
    return `<path d="${line}" fill="none" stroke="#3a2c58" stroke-width="1"/><g filter="url(#c-neon)">${dots}</g>`;
}

/** Things on top of a roof: antennas and server boxes, water tanks, gardens or plain AC units. */
function drawRoofProps(b) {
    const r = b.roof;
    const x0 = r ? b.x0 + r.inset : b.x0;
    const x1 = r ? b.x1 - r.inset : b.x1;
    const y0 = r ? b.y0 + r.inset : b.y0;
    const y1 = r ? b.y1 - r.inset : b.y1;
    const z = b.h + (r?.h ?? 0);
    if (x1 - x0 < 2.4 || y1 - y0 < 2.4) return '';

    const near = b.side === 'near';
    const roll = rnd();
    const theme = b.h > 9
        ? (roll < 0.45 ? 'tech' : roll < 0.7 ? 'tank' : 'plain')
        : (roll < 0.3 ? 'garden' : roll < 0.55 ? 'tank' : roll < 0.75 ? 'tech' : 'plain');
    const spot = (fx, fy) => [x0 + (x1 - x0) * fx, y0 + (y1 - y0) * fy];
    const props = [];

    if (theme === 'garden') {
        props.push({ k: -1, draw: () => drawGarden(x0, x1, y0, y1, z) });
        props.push({ k: 99, draw: () => drawStringLights(x0, x1, y0, y1, z) });
    } else if (theme === 'tech') {
        const [mx, my] = spot(between(0.25, 0.4), between(0.25, 0.4));
        props.push({ k: mx + my, draw: () => drawMast(mx, my, z) });
        const [dx, dy] = spot(between(0.6, 0.75), between(0.2, 0.35));
        props.push({ k: dx + dy, draw: () => drawDish(dx, dy, z) });
        if (rnd() < 0.6) {
            const [sx, sy] = spot(0.25, 0.55);
            props.push({ k: sx + sy + 1, draw: () => drawServerBox(sx, sy, z) });
        }
    } else if (theme === 'tank') {
        const [tx, ty] = spot(between(0.3, 0.45), between(0.3, 0.45));
        props.push({ k: tx + ty, draw: () => drawWaterTank(tx, ty, z) });
        const [ax, ay] = spot(0.62, 0.62);
        props.push({ k: ax + ay, draw: () => drawAcUnit(ax, ay, z, near) });
    } else {
        for (let i = Math.floor(between(1, 3.5)); i > 0; i--) {
            const [ax, ay] = spot(between(0.15, 0.7), between(0.15, 0.7));
            props.push({ k: ax + ay, draw: () => drawAcUnit(ax, ay, z, near) });
        }
        if (!r && rnd() < (near ? 0.4 : 0.15)) {
            props.push({ k: 99, draw: () => drawStringLights(x0, x1, y0, y1, z) });
        }
    }
    return props.sort((a, c) => a.k - c.k).map(p => p.draw()).join('');
}

/** LED panel on the hero tower's side showing how long the farm has been running. */
function drawClock() {
    const { x0, x1, z0, z1 } = CLOCK;
    const w = (x1 - x0) * S;
    const h = (z1 - z0) * S;
    return `<g transform="${faceYMatrix(x0, 1.5 + 0.02, z1)}">`
        + `<rect x="-6" y="-6" width="${fmt(w + 12)}" height="${fmt(h + 12)}" rx="6" fill="#0d0a1c"/>`
        + `<rect x="-3" y="-3" width="${fmt(w + 6)}" height="${fmt(h + 6)}" rx="5" fill="none" stroke="#7fe3ff" stroke-opacity="0.35" stroke-width="1.5"/>`
        + `<text data-uptime-label x="${fmt(w / 2)}" y="22" text-anchor="middle" font-family="Inter, sans-serif" font-weight="600" font-size="15" letter-spacing="5" fill="#ffb3d1" filter="url(#c-neon)"></text>`
        + `<text data-uptime x="${fmt(w / 2)}" y="${fmt(h - 12)}" text-anchor="middle" font-family="ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" font-weight="700" font-size="28" fill="#8ff6ff" filter="url(#c-neon)">00.00:00:00</text>`
        + '</g>';
}

// Matrix that maps a flat picture onto the road-facing (+x) plane at x, with its
// top-left corner at (y, z). One picture pixel is 1/S world units.
function faceXMatrix(x, y, z, scale = 1) {
    const [ox, oy] = project(x, y, z);
    return `matrix(${fmt(0.866 * scale * 1000) / 1000} ${-0.5 * scale} 0 ${scale} ${fmt(ox)} ${fmt(oy)})`;
}

function faceYMatrix(x, y, z) {
    const [ox, oy] = project(x, y, z);
    return `matrix(0.866 0.5 0 1 ${fmt(ox)} ${fmt(oy)})`;
}

function drawDurov() {
    const x = -4.2 + 0.02;
    const { y0, y1, z0, z1 } = DUROV;
    const w = (y1 - y0) * S;
    const h = (z1 - z0) * S;
    let out = `<g transform="${faceXMatrix(x, y1, z1)}">`;
    out += `<rect x="-10" y="-10" width="${fmt(w + 20)}" height="${fmt(h + 20)}" rx="4" fill="#120c22"/>`;
    out += `<image href="img/durov.jpg" width="${fmt(w)}" height="${fmt(h)}" preserveAspectRatio="xMidYMid slice"/>`;
    out += `<rect width="${fmt(w)}" height="${fmt(h)}" fill="#ff6aa8" opacity="0.08"/>`;
    out += `<rect class="neon-buzz" x="-6" y="-6" width="${fmt(w + 12)}" height="${fmt(h + 12)}" rx="5" fill="none" stroke="#ff6fb1" stroke-width="3" filter="url(#c-neon)"/>`;
    out += `<text class="neon-buzz" x="${fmt(w / 2)}" y="-26" text-anchor="middle" font-family="Inter, sans-serif" font-weight="700" font-size="30" letter-spacing="5" fill="#a8f4ff" filter="url(#c-neon)">BOT FARM</text>`;
    out += '</g>';
    // Spotlights under the billboard.
    for (const y of [y0 + 0.8, y1 - 0.8]) {
        const [lx, ly] = project(x + 0.5, y, z0 - 0.5);
        out += `<circle cx="${fmt(lx)}" cy="${fmt(ly)}" r="3" fill="#fff1d8"/>`;
    }
    return out;
}

// ---- Street furniture -----------------------------------------------------

function isoCircle(x, y, r, n = 18) {
    const list = [];
    for (let i = 0; i < n; i++) {
        const t = (i / n) * Math.PI * 2;
        list.push([x + Math.cos(t) * r, y + Math.sin(t) * r, 0]);
    }
    return list;
}

function lamps(side) {
    const x = side * (ROAD + 0.45);
    const result = [];
    for (let y = Y_MIN + (side > 0 ? 3.5 : 0); y < Y_MAX; y += 7) {
        result.push({ x, y, side });
    }
    return result;
}

function drawLampPool(l) {
    return `<polygon class="screen" points="${points(isoCircle(l.x - l.side * 1.3, l.y, 2.6))}" fill="url(#c-pool)"/>`;
}

function drawLamp(l) {
    const [bx, by] = project(l.x, l.y, CURB);
    const [tx, ty] = project(l.x, l.y, 3.3);
    const [hx, hy] = project(l.x - l.side * 0.8, l.y, 3.2);
    return `<path d="M${fmt(bx)} ${fmt(by)}L${fmt(tx)} ${fmt(ty)}L${fmt(hx)} ${fmt(hy)}" fill="none" stroke="#2f2547" stroke-width="2.5" stroke-linecap="round"/>`
        + `<circle class="screen" cx="${fmt(hx)}" cy="${fmt(hy)}" r="26" fill="url(#c-lamp)"/>`
        + `<circle cx="${fmt(hx)}" cy="${fmt(hy + 1.5)}" r="2.6" fill="#fff3df"/>`;
}

function drawTree(x, y, z0 = 0, scale = 1) {
    const r = 0.36 * scale;
    const t = 0.07 * scale;
    const base = z0 + 0.75 * scale;
    const apex = [x, y, base + between(1, 1.4) * scale];
    const right = [[x + r, y - r, base], [x + r, y + r, base], apex];
    const front = [[x - r, y + r, base], [x + r, y + r, base], apex];
    const top = base + 0.1 * scale;
    const trunkRight = [[x + t, y - t, z0], [x + t, y + t, z0], [x + t, y + t, top], [x + t, y - t, top]];
    const trunkFront = [[x - t, y + t, z0], [x + t, y + t, z0], [x + t, y + t, top], [x - t, y + t, top]];
    return `<polygon points="${points(trunkRight)}" fill="hsl(20 24% 30%)"/>`
        + `<polygon points="${points(trunkFront)}" fill="hsl(15 24% 19%)"/>`
        + `<polygon points="${points(right)}" fill="hsl(168 34% 26%)"/>`
        + `<polygon points="${points(front)}" fill="hsl(182 36% 16%)"/>`;
}

function drawRoad() {
    let out = '';
    const strip = (x0, x1, fill, opacity = 1, z = 0) =>
        `<polygon points="${points([[x0, Y_MIN, z], [x1, Y_MIN, z], [x1, Y_MAX, z], [x0, Y_MAX, z]])}" fill="${fill}" opacity="${opacity}"/>`;
    out += strip(-ROAD, ROAD, '#1b162b');
    // Sidewalks sit a curb above the road; the far curb's face is the one the view can see.
    out += strip(-4.2, -ROAD, '#2c2545', 1, CURB);
    out += `<polygon points="${points([[-ROAD, Y_MIN, 0], [-ROAD, Y_MAX, 0], [-ROAD, Y_MAX, CURB], [-ROAD, Y_MIN, CURB]])}" fill="#4a3d68"/>`;
    out += strip(ROAD, 5.4, '#2c2545', 1, CURB);
    out += strip(-ROAD - 0.06, -ROAD + 0.06, '#c58fd8', 0.35);
    out += strip(ROAD - 0.06, ROAD + 0.06, '#c58fd8', 0.35);
    out += strip(-2.9, -2.82, '#e9dcff', 0.3);
    out += strip(2.82, 2.9, '#e9dcff', 0.3);
    out += strip(-0.14, -0.06, '#ffc27a', 0.65);
    out += strip(0.06, 0.14, '#ffc27a', 0.65);
    let dashes = '';
    for (const x of [-1.5, 1.5]) {
        for (let y = Y_MIN; y < Y_MAX; y += 3) {
            dashes += pathOf([[x - 0.05, y, 0], [x + 0.05, y, 0], [x + 0.05, y + 1.3, 0], [x - 0.05, y + 1.3, 0]]);
        }
    }
    out += `<path d="${dashes}" fill="#e9dcff" opacity="0.4"/>`;
    return out;
}

const BRIDGE = { x0: -4.2, x1: 4.6, y0: -13.4, y1: -12.2, z0: 3.9, z1: 4.6 };
const POSTERS = ['☕', '🍜', '🎧', '📡', '🌙', '🎮'];
const POSTER_COLORS = ['#ff6fb1', '#ffb36b', '#7fe3ff', '#b99bff', '#ff8a8a', '#8affc1'];

function drawBridge() {
    const { x0, x1, y0, y1, z0, z1 } = BRIDGE;
    let out = drawBox(4.0, 4.6, y0 + 0.15, y1 - 0.15, 0, z0, { lit: '#3a2a52', side: '#251c3b', top: '#3a2a52' }, false);
    out += drawBox(x0, x1, y0, y1, z0, z1 - z0, { lit: '#4a3263', side: '#3b2a55', top: '#2e2446' }, false);
    // Railing.
    const rail = [[x0, y1, z1 + 0.45], [x1, y1, z1 + 0.45]];
    out += `<polyline points="${points(rail)}" fill="none" stroke="#ff9fc4" stroke-opacity="0.6" stroke-width="1.5"/>`;
    for (let x = x0 + 0.4; x < x1; x += 0.7) {
        out += `<polyline points="${points([[x, y1, z1], [x, y1, z1 + 0.45]])}" fill="none" stroke="#ff9fc4" stroke-opacity="0.35" stroke-width="1"/>`;
    }
    // Posters hanging on the front side.
    out += `<g transform="${faceYMatrix(x0 + 0.5, y1 + 0.01, z0 + 0.25)}">`;
    const count = POSTERS.length;
    const span = (x1 - x0 - 1) * S;
    const pw = span / count - 10;
    POSTERS.forEach((emoji, i) => {
        const px = i * (pw + 10);
        const color = POSTER_COLORS[i];
        out += `<rect x="${fmt(px)}" y="0" width="${fmt(pw)}" height="50" rx="3" fill="#1b1430" stroke="${color}" stroke-width="2" filter="url(#c-neon)"/>`;
        out += `<text x="${fmt(px + pw / 2)}" y="36" text-anchor="middle" font-size="26" font-family="Apple Color Emoji, Segoe UI Emoji, Noto Color Emoji, sans-serif">${emoji}</text>`;
    });
    out += '</g>';
    return out;
}

// ---- Scene ----------------------------------------------------------------

function buildScene(svg) {
    rnd = random(SEED);
    signCount = 0;
    shopSpills = [];
    const buildings = makeCity();
    // Painter's order. The view looks along +x and +y, so a box that lies entirely at
    // smaller x (or, failing that, smaller y) than another can never cover it.
    const byDepth = (a, b) => {
        if (a.x1 <= b.x0) return -1;
        if (b.x1 <= a.x0) return 1;
        if (a.y1 <= b.y0) return -1;
        if (b.y1 <= a.y0) return 1;
        return (a.x0 + a.x1 + a.y0 + a.y1) - (b.x0 + b.x1 + b.y0 + b.y1);
    };
    const farLamps = lamps(-1);
    const nearLamps = lamps(1);

    let back = `<rect width="${VIEW_W}" height="${VIEW_H}" fill="#221c36"/>`;
    const farItems = buildings.filter(b => b.side === 'far').map(b => ({ ...b, draw: () => drawBuilding(b) }));
    back += farItems.sort(byDepth).map(i => i.draw()).join('');
    back += `<rect width="${VIEW_W}" height="${VIEW_H}" fill="url(#c-haze)"/>`;

    let road = drawRoad();
    road += [...farLamps, ...nearLamps].map(drawLampPool).join('');
    road += shopSpills.map(drawShopSpill).join('');
    // Far sidewalk trees and lamps stand on the sidewalk, so they go after it.
    const farStreet = farLamps.map(l => ({ x0: l.x, x1: l.x, y0: l.y, y1: l.y, draw: () => drawLamp(l) }));
    for (let y = Y_MIN + 3.5; y < Y_MAX; y += 7) {
        farStreet.push({ x0: -3.9, x1: -3.5, y0: y, y1: y + 0.4, draw: () => drawTree(-3.7, y, CURB) });
    }
    road += farStreet.sort(byDepth).map(i => i.draw()).join('');

    const nearItems = buildings.filter(b => b.side === 'near').map(b => ({ ...b, draw: () => drawBuilding(b) }));
    for (let y = Y_MIN; y < Y_MAX; y += 7) {
        nearItems.push({ x0: 3.6, x1: 4.0, y0: y, y1: y + 0.4, draw: () => drawTree(3.8, y, CURB) });
    }
    for (const l of nearLamps) {
        nearItems.push({ x0: l.x, x1: l.x, y0: l.y, y1: l.y, draw: () => drawLamp(l) });
    }
    const front = nearItems.sort(byDepth).map(i => i.draw()).join('')
        + `<rect width="${VIEW_W}" height="${VIEW_H}" fill="url(#c-vignette)" pointer-events="none"/>`;

    svg.innerHTML = DEFS
        + `<g>${back}</g><g>${road}</g>`
        + LANES.map((_, i) => `<g data-lane="${i}"></g>`).join('')
        + `<g>${drawBridge()}</g>`
        + `<g>${front}</g>`;
}

// ---- Traffic --------------------------------------------------------------

const UNDERGLOWS = ['c-under-pink', 'c-under-cyan', 'c-under-violet'];

function makeCar(bot, lane, navigate) {
    const g = document.createElementNS(SVG_NS, 'g');
    g.setAttribute('class', 'car');
    const dir = lane.dir;
    const beam = [delta(-0.35, dir * 0.55), delta(0.35, dir * 0.55), delta(1.1, dir * 4.8), delta(-1.1, dir * 4.8)];
    const tails = [delta(-0.32, -dir * 0.55, 0.35), delta(0.32, -dir * 0.55, 0.35)];
    const glow = UNDERGLOWS[Math.floor(Math.random() * UNDERGLOWS.length)];
    const mirror = dir < 0 ? ' transform="scale(-1 1)"' : '';
    const title = document.createElementNS(SVG_NS, 'title');
    title.textContent = bot.name;

    g.innerHTML = `<polygon class="screen" points="${beam.map(p => p.map(fmt).join(',')).join(' ')}" fill="url(#c-beam-${dir > 0 ? 'down' : 'up'})"/>`
        + `<ellipse class="screen" cx="0" cy="2" rx="30" ry="13" fill="url(#${glow})"/>`
        + `<ellipse cx="0" cy="2" rx="15" ry="6" fill="#05030f" opacity="0.45"/>`
        + tails.map(([x, y]) => `<circle class="screen" cx="${fmt(x)}" cy="${fmt(y)}" r="7" fill="url(#c-tail)"/>`).join('')
        + `<g class="bob"><text y="-4"${mirror}></text></g>`;
    g.querySelector('text').textContent = bot.emoji;
    g.prepend(title);
    g.addEventListener('click', () => navigate(bot.url));
    return g;
}

/** On screens narrower than the scene, crops around the tower instead of the middle. */
function fitFrame(svg, host) {
    const { width, height } = host.getBoundingClientRect();
    if (!width || !height) return;
    const visible = Math.min(VIEW_W, VIEW_H * (width / height));
    const left = Math.min(Math.max(FOCUS_X - visible / 2, 0), VIEW_W - visible);
    svg.setAttribute('viewBox', `${fmt(left)} 0 ${fmt(visible)} ${VIEW_H}`);
}

function formatUptime(totalSeconds) {
    const t = Math.max(0, Math.floor(totalSeconds));
    const pad = n => String(n).padStart(2, '0');
    return `${pad(Math.floor(t / 86400))}.${pad(Math.floor(t / 3600) % 24)}:${pad(Math.floor(t / 60) % 60)}:${pad(t % 60)}`;
}

/** Keeps the uptime panel ticking, counting from the server's uptime at mount time. */
function startClock(svg, uptime) {
    const label = svg.querySelector('[data-uptime-label]');
    const value = svg.querySelector('[data-uptime]');
    label.textContent = uptime.label.toLocaleUpperCase();
    const started = performance.now();
    const update = () => {
        value.textContent = formatUptime(uptime.seconds + (performance.now() - started) / 1000);
    };
    update();
    const timer = setInterval(update, 1000);
    return () => clearInterval(timer);
}

/**
 * Draws the scene into host and starts the traffic.
 * bots: [{ name, emoji, url }]; uptime: { seconds, label } for the panel on the tower.
 */
export function mount(host, bots, uptime) {
    const svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', `0 0 ${VIEW_W} ${VIEW_H}`);
    svg.setAttribute('preserveAspectRatio', 'xMidYMid slice');
    svg.setAttribute('aria-hidden', 'true');
    host.replaceChildren(svg);
    buildScene(svg);
    const stopClock = startClock(svg, uptime);
    const frameObserver = new ResizeObserver(() => fitFrame(svg, host));
    frameObserver.observe(host);

    const laneGroups = LANES.map((_, i) => svg.querySelector(`[data-lane="${i}"]`));
    const navigate = url => window.Blazor?.navigateTo ? window.Blazor.navigateTo(url) : (window.location.href = url);
    const cars = [];
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const place = car => {
        const [sx, sy] = project(car.lane.x, car.y, 0);
        car.el.setAttribute('transform', `translate(${fmt(sx)} ${fmt(sy)})`);
        car.bob.setAttribute('transform', `translate(0 ${fmt(Math.sin(car.y * 2.1) * 1.2)})`);
    };

    const spawn = (y = null) => {
        if (bots.length === 0) return;
        const laneIndex = Math.floor(Math.random() * LANES.length);
        const lane = LANES[laneIndex];
        const startY = y ?? (lane.dir > 0 ? Y_MIN : Y_MAX);
        if (cars.some(c => c.lane === lane && Math.abs(c.y - startY) < 7)) return;
        const bot = bots[Math.floor(Math.random() * bots.length)];
        const el = makeCar(bot, lane, navigate);
        const car = { el, bob: el.querySelector('.bob'), lane, y: startY };
        // Nearer cars (larger y) draw on top of farther ones in the same lane.
        const group = laneGroups[laneIndex];
        const after = [...group.children].find(child => cars.find(c => c.el === child)?.y > startY);
        group.insertBefore(el, after ?? null);
        cars.push(car);
        place(car);
    };

    if (reducedMotion) {
        [-8, 4, 14].forEach(y => spawn(y));
        return {
            dispose: () => {
                stopClock();
                frameObserver.disconnect();
                host.replaceChildren();
            },
        };
    }

    // A couple of cars already on the road so the street is not empty at first.
    spawn(between(-14, 0));
    spawn(between(4, 16));

    let last = performance.now();
    let nextSpawn = 1.5;
    let frame = 0;
    const tick = now => {
        const dt = Math.min((now - last) / 1000, 0.1);
        last = now;
        nextSpawn -= dt;
        if (nextSpawn <= 0) {
            spawn();
            nextSpawn = 2 + Math.random() * 4.5;
        }
        for (let i = cars.length - 1; i >= 0; i--) {
            const car = cars[i];
            car.y += car.lane.dir * car.lane.speed * dt;
            if (car.y < Y_MIN - 2 || car.y > Y_MAX + 2) {
                car.el.remove();
                cars.splice(i, 1);
            } else {
                place(car);
            }
        }
        frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);

    return {
        dispose: () => {
            cancelAnimationFrame(frame);
            stopClock();
            frameObserver.disconnect();
            host.replaceChildren();
        },
    };
}
