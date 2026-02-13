(() => {
  const form = document.querySelector("[data-search-form]");
  if (!form) {
    return;
  }

  const input = form.querySelector("[data-search-input]");
  const panel = form.querySelector("[data-search-suggestions]");

  if (!input || !panel) {
    return;
  }

  const suggestionsUrl = input.dataset.suggestionsUrl;
  const configuredMin = parseInt(input.dataset.suggestionsMin || "2", 10);
  let minLength = Number.isFinite(configuredMin) ? configuredMin : 2;
  let debounceId;
  let controller;

  const hidePanel = () => {
    panel.hidden = true;
    panel.innerHTML = "";
  };

  const escapeHtml = (value) => {
    const div = document.createElement("div");
    div.textContent = value ?? "";
    return div.innerHTML;
  };

  const renderSection = (label, itemsHtml) => {
    return `<div class="suggestion-section">
        <div class="suggestion-label">${label}</div>
        ${itemsHtml}
    </div>`;
  };

  const renderSuggestions = (payload) => {
    if (!payload) {
      hidePanel();
      return;
    }

    if (payload.minLength && Number.isFinite(payload.minLength)) {
      minLength = Math.max(minLength, payload.minLength);
    }

    const queries = payload.queries || [];
    const categories = payload.categories || [];
    const products = payload.products || [];

    if (!queries.length && !categories.length && !products.length) {
      hidePanel();
      return;
    }

    const sections = [];

    if (queries.length) {
      const items = queries
        .map(
          (q) => `<div class="suggestion-item" data-type="query" data-value="${escapeHtml(q)}">
              <span class="suggestion-title">${escapeHtml(q)}</span>
          </div>`
        )
        .join("");
      sections.push(renderSection("Suggested searches", items));
    }

    if (categories.length) {
      const items = categories
        .map(
          (c) => `<div class="suggestion-item" data-type="category" data-id="${escapeHtml(
            c.id
          )}">
              <div>
                  <div class="suggestion-title">${escapeHtml(c.name)}</div>
                  <div class="suggestion-meta">Category</div>
              </div>
              <span class="text-primary fw-semibold">View</span>
          </div>`
        )
        .join("");
      sections.push(renderSection("Categories", items));
    }

    if (products.length) {
      const items = products
        .map(
          (p) => `<div class="suggestion-item" data-type="product" data-id="${escapeHtml(
            p.id
          )}">
              <div>
                  <div class="suggestion-title">${escapeHtml(p.title)}</div>
                  <div class="suggestion-meta">${escapeHtml(p.category)} · ${new Intl.NumberFormat(undefined, {
            style: "currency",
            currency: "USD",
            maximumFractionDigits: 0,
          }).format(p.price)}</div>
              </div>
              <span class="text-primary fw-semibold">Open</span>
          </div>`
        )
        .join("");
      sections.push(renderSection("Products", items));
    }

    panel.innerHTML = sections.join("");
    panel.hidden = false;
  };

  const fetchSuggestions = (term) => {
    if (!suggestionsUrl) {
      return;
    }

    if (controller) {
      controller.abort();
    }

    controller = new AbortController();

    fetch(`${suggestionsUrl}?q=${encodeURIComponent(term)}`, {
      signal: controller.signal,
    })
      .then((response) => (response.ok ? response.json() : null))
      .then((data) => renderSuggestions(data))
      .catch((err) => {
        if (err.name === "AbortError") {
          return;
        }
        hidePanel();
      });
  };

  input.addEventListener("input", () => {
    const value = input.value.trim();
    if (!value || value.length < minLength) {
      hidePanel();
      return;
    }

    clearTimeout(debounceId);
    debounceId = setTimeout(() => fetchSuggestions(value), 200);
  });

  input.addEventListener("focus", () => {
    const value = input.value.trim();
    if (value.length >= minLength && panel.innerHTML) {
      panel.hidden = false;
    }
  });

  panel.addEventListener("click", (event) => {
    const item = event.target.closest(".suggestion-item");
    if (!item) {
      return;
    }

    const type = item.dataset.type;
    if (type === "query") {
      input.value = item.dataset.value || "";
      form.requestSubmit ? form.requestSubmit() : form.submit();
      hidePanel();
      return;
    }

    if (type === "category" && item.dataset.id) {
      window.location.href = `/Categories/${item.dataset.id}`;
      return;
    }

    if (type === "product" && item.dataset.id) {
      window.location.href = `/Products/Details?id=${item.dataset.id}`;
    }
  });

  document.addEventListener("click", (event) => {
    if (!form.contains(event.target)) {
      hidePanel();
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      hidePanel();
    }
  });

  form.addEventListener("submit", hidePanel);
})();
