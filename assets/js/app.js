const THEME_BUTTONS = [
  { id: "light-theme-button", theme: "light" },
  { id: "dark-theme-button", theme: "dark" },
  { id: "system-theme-button", theme: "system" },
]

function applyTheme(theme) {
  try {
    localStorage.setItem("theme", theme)
  } catch (e) {
    console.warn("localStorage not available:", e)
  }
  const dark =
    theme === "dark" ||
    (theme === "system" && window.matchMedia("(prefers-color-scheme: dark)").matches)
  window.__theme?.toggle(dark)
}

function updateThemeButtonStates() {
  let stored = null
  try {
    stored = localStorage.theme ?? null
  } catch {
    // localStorage unavailable
  }
  const active = stored === "light" || stored === "dark" ? stored : "system"
  for (const { id, theme } of THEME_BUTTONS) {
    document.getElementById(id)?.setAttribute("aria-pressed", String(theme === active))
  }
}

function initThemeToggle() {
  for (const { id, theme } of THEME_BUTTONS) {
    document.getElementById(id)?.addEventListener("click", () => {
      applyTheme(theme)
      updateThemeButtonStates()
    })
  }
  updateThemeButtonStates()
}

function onScroll() {
  document.documentElement.classList.toggle("scrolled", window.scrollY > 0)
}

function initBackToTop() {
  document.getElementById("back-to-top")?.addEventListener("click", (event) => {
    event.preventDefault()
    window.scrollTo({ top: 0, behavior: "smooth" })
  })
}

// Screen readers can't see the copy buttons' icon swap, so success is also announced in the layout's
// role="status" region.
function announce(message) {
  const status = document.getElementById("status")
  if (!status) return
  status.textContent = message
  setTimeout(() => (status.textContent = ""), 2000)
}

function initCopyLinkButton() {
  const button = document.getElementById("copy-link-button")
  const icon = document.getElementById("copy-link-icon")
  const check = document.getElementById("copy-link-check")
  if (!button || !icon || !check) return

  button.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(window.location.href)
    } catch {
      return
    }
    announce("Link copied")
    icon.classList.add("hidden")
    check.classList.remove("hidden")
    setTimeout(() => {
      check.classList.add("hidden")
      icon.classList.remove("hidden")
    }, 2000)
  })
}

const COPY_ICON = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>`

const CHECK_ICON = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>`

function initCopyCodeButtons() {
  document.querySelectorAll("article pre code").forEach((codeBlock) => {
    const pre = codeBlock.parentElement
    if (!pre || pre.querySelector(".copy-code-button")) return

    const button = document.createElement("button")
    button.className = "copy-code-button"
    button.setAttribute("aria-label", "Copy code")
    button.innerHTML = COPY_ICON

    button.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(codeBlock.textContent ?? "")
      } catch {
        return
      }
      announce("Code copied")
      button.innerHTML = CHECK_ICON
      setTimeout(() => (button.innerHTML = COPY_ICON), 2000)
    })

    pre.appendChild(button)
  })
}

function initReadingProgress() {
  const bar = document.getElementById("reading-progress")
  if (!bar) return
  if (CSS.supports("animation-timeline: scroll()")) return

  function update() {
    const docHeight = document.documentElement.scrollHeight - window.innerHeight
    const progress = docHeight > 0 ? Math.min((window.scrollY / docHeight) * 100, 100) : 0
    bar.style.width = `${progress}%`
  }

  window.addEventListener("scroll", update, { passive: true })
  update()
}

function init() {
  onScroll()
  document.addEventListener("scroll", onScroll, { passive: true })

  initThemeToggle()
  initBackToTop()
  initCopyLinkButton()
  initCopyCodeButtons()
  initReadingProgress()
}

document.addEventListener("DOMContentLoaded", init)
