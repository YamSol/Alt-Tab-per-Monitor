# 0003. Answers to the spec's open questions (§7)

- Status: accepted, each answer is configurable or reversible
- Date: 2026-10-22

## 1. Owned window of a hidden owner: follows the owner

`OwnedFollowsOwner = true` (settings.json). When an owner is hidden, its titled, non-modal owned windows on the current monitor are hidden too, recursively. That way no orphaned palette or "Find" dialog is left floating alone.

Windows kept for their own reason stay protected: modal, untitled, tool windows and excluded processes. Owned windows *on another monitor* are still judged independently (criterion 6).

Covered by `HidePlannerTests.Owned_window_follows_hidden_owner_when_enabled` and the tests next to it.

## 2. Microsoft Store apps (`ApplicationFrameHost`): no special rule

The visible frame is an ordinary top-level window, and hiding it hides the app. Suspended or background frames are cloaked and therefore kept (`Reason.Cloaked`). `--list-all` on a real desktop shows both cases.

## 3. Exclusion list: starts empty

`ExcludedProcesses = []`. To find apps that misbehave under `SW_HIDE`, use view-only mode, the tray's "Show current plan..." or `--list`, plus the log. Then add them via the tray's "Exclusions...". (View-only was the default until [0004](0004-active-by-default.md).)
