// Production service worker — սա է, որ Gialora-ն դարձնում է տեղադրվող հավելված։
//
// service-worker-assets.js-ը գեներացվում է publish-ի ժամանակ (<ServiceWorkerAssetsManifest>)
// և պարունակում է բոլոր ֆայլերի ցանկը՝ integrity hash-երով։
self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => {
    // Էջը "Թարմացնել"-ը սեղմելիս խնդրում է նոր worker-ին անմիջապես ստանձնել։
    if (event.data?.type === 'SKIP_WAITING')
        self.skipWaiting();
});

const cacheNamePrefix = 'gialora-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;

const offlineAssetsInclude = [
    /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/,
    /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.svg$/,
    /\.webmanifest$/, /\.blat$/, /\.dat$/
];

// service-worker.js-ը ինքն իրեն cache անելը worker-ը կսառեցներ հին տարբերակի վրա։
const offlineAssetsExclude = [/^service-worker\.js$/];

// index.html-ի <base href="/" />-ին համապատասխան։ Ենթապանակից hosting անելիս փոխիր։
const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall() {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate() {
    // Հին տարբերակների cache-երը ջնջում ենք, որ պահոցը անվերջ չաճի։
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    // API-ի հարցումները ԵՐԲԵՔ չեն cache-վում։ Ճաշացանկերը, գնումների ցուցակները և
    // ընտանիքի տվյալները ուղիղ Gialora.Api-ից են — հնացած պատասխանը այստեղ
    // ավելի վատ է, քան սխալի հաղորդագրությունը։ API-ն ուրիշ origin-ի վրա է։
    if (new URL(event.request.url).origin !== self.origin)
        return fetch(event.request);

    if (event.request.method !== 'GET')
        return fetch(event.request);

    // Ցանկացած ներքին navigation-ի դեպքում վերադարձնում ենք index.html-ը —
    // Blazor-ի router-ը ինքը կբացի /weekly, /recipes/{slug} և մնացածը։
    const shouldServeIndexHtml = event.request.mode === 'navigate'
        && !manifestUrlList.some(url => url === event.request.url);

    const request = shouldServeIndexHtml ? 'index.html' : event.request;
    const cache = await caches.open(cacheName);
    const cachedResponse = await cache.match(request);

    if (cachedResponse)
        return cachedResponse;

    try {
        return await fetch(event.request);
    } catch {
        // Offline ենք, և ֆայլը cache-ում չկա։ navigation-ի դեպքում գոնե
        // հավելվածի կմախքը բացենք, որ browser-ի "no internet" էջը չերևա։
        if (event.request.mode === 'navigate') {
            const shell = await cache.match('index.html');
            if (shell) return shell;
        }
        throw new Error(`Gialora: offline and "${event.request.url}" is not cached.`);
    }
}
