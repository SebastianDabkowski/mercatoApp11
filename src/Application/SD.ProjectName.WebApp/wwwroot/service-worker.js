self.addEventListener("install", (event) => {
  event.waitUntil(self.skipWaiting());
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("push", (event) => {
  if (!event.data) {
    return;
  }

  let payload;
  try {
    payload = event.data.json();
  } catch {
    payload = { title: "Mercato notification", body: event.data.text() };
  }

  const title = payload.title || "Mercato notification";
  const options = {
    body: payload.body || payload.description || "",
    badge: "/favicon.ico",
    icon: "/favicon.ico",
    data: {
      url: payload.url || payload.targetUrl || "/",
      notificationId: payload.notificationId,
      category: payload.category,
    },
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener("notificationclick", (event) => {
  const targetUrl = event.notification?.data?.url || "/";
  event.notification?.close();

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
      const existing = clients.find((client) => client.url.includes(new URL(targetUrl, self.location.origin).pathname));
      if (existing) {
        existing.focus();
        existing.navigate(targetUrl);
        return;
      }

      self.clients.openWindow(targetUrl);
    })
  );
});
