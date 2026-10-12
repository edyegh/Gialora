// Development service worker — ԴԻՏԱՎՈՐՅԱԼ ոչինչ չի անում։
//
// Publish-ի ժամանակ այս ֆայլը ՓՈԽԱՐԻՆՎՈՒՄ Է service-worker.published.js-ով
// (տես .csproj-ի <ServiceWorker Include=... PublishedContent=... />)։
//
// Եթե dev-ում էլ cache անեինք, `dotnet run`-ից հետո ամեն փոփոխություն
// թաքնվելու էր հին cache-ի տակ, և մենք կկարգավորեինք արդեն վերագրած կոդը։
self.addEventListener('fetch', () => { });
