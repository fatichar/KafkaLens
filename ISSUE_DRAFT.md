# Overhaul Edit Clusters Dialog

## Problem
The current "Edit Clusters" dialog has several usability issues and functional limitations:
1.  **Stuck in Edit Mode:** Selecting a cluster puts the dialog in "edit mode" with no clear way to return to "add mode".
2.  **Limited Scope:** It only supports editing direct Kafka clusters (`cluster_info.json`). It does not support KafkaLens clients (`client_info.json`).
3.  **UI:** The inline editing experience is confusing.
4.  **Updates:** Changes (adds/edits) are not always immediately reflected in the main application.

## Goals
Refactor the "Edit Clusters" dialog into a modern, fully functional management interface.

### Requirements

#### 1. UI Structure
*   **Tabbed Interface:** Use tabs to separate:
    *   **Direct Clusters**: Managed via `cluster_info.json`.
    *   **KafkaLens Clients**: Managed via `client_info.json`.
*   **List View:** Each tab should display a list (DataGrid) of configured items.
*   **Actions:**
    *   **Add:** Open a modal popup to add a new item.
    *   **Edit:** Open a modal popup to edit the selected item.
    *   **Remove:** Delete the selected item.

#### 2. KafkaLens Clients Support
*   Enable full CRUD operations for KafkaLens Clients.
*   Fields: Name, Address, Protocol (Default: `grpc`).

#### 3. Immediate Updates
*   Ensure that any additions, edits, or removals are immediately reflected in the main application's cluster tree without needing a restart.
*   Fix `ClientFactory` to properly reload clients.

#### 4. Implementation Details
*   **View:** `EditClustersDialog.axaml` (Rename/Update).
*   **ViewModel:** `EditClustersViewModel.cs` (Rename from `LocalClustersViewModel`).
*   **Popups:** Create `AddEditClusterDialog` and `AddEditClientDialog`.

## Acceptance Criteria
- [ ] Can add, edit, and remove Direct Clusters via a popup dialog.
- [ ] Can add, edit, and remove KafkaLens Clients via a popup dialog.
- [ ] Changes are persisted to `cluster_info.json` and `client_info.json`.
- [ ] Main application updates immediately after closing the dialog or saving changes.
- [ ] "Stuck in edit mode" issue is resolved.
