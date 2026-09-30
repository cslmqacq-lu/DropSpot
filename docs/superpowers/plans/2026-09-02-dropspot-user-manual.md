# DropSpot User Manual Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a single Chinese PNG that teaches a first-time user how to configure and use DropSpot 1.0.7.

**Architecture:** Keep live UI captures as separate assets, compose them with a fixed 1800 x 3800 HTML layout, and render the HTML with bundled Chromium. The HTML remains the editable source and the PNG is the distribution artifact.

**Tech Stack:** HTML, CSS, Playwright Chromium, Windows Computer Use screenshots.

## Global Constraints

- Use authentic DropSpot 1.0.7 screenshots for the primary interface demonstrations.
- Keep screenshots at their natural aspect ratio.
- Include Ctrl+Alt+F and Ctrl+Alt+D exactly.
- Do not modify application source code or existing release artifacts.

---

### Task 1: Capture source screens

**Files:**
- Create: `deliverables/manual/screenshots/01-main-window.jpg`
- Create: `deliverables/manual/screenshots/02-expanded-files.jpg`
- Create: `deliverables/manual/screenshots/03-settings-general.jpg`
- Create: `deliverables/manual/screenshots/04-monitor-drives.jpg`

- [x] Capture the running 1.0.7 main window.
- [x] Capture one expanded active-folder card.
- [x] Capture general and monitor-drive settings.

### Task 2: Compose the manual

**Files:**
- Create: `deliverables/manual/manual.html`

- [x] Build the 1800 x 3800 layout with product, setup, workflow, floating, shortcut, and tray sections.
- [x] Reference local screenshots without resizing distortion.
- [x] Add numbered instructions and operation labels.

### Task 3: Render and verify

**Files:**
- Create: `deliverables/manual/DropSpot-使用说明书-v1.0.7.png`

- [x] Render the HTML at 1800 x 3800 with bundled Chromium.
- [x] Inspect the full image and detail crops for clipping, overlap, and readability.
- [x] Correct the source and rerender until visual QA passes.
