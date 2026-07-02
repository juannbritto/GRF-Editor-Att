# GRF Editor Safe — Classic Refined Interface Design

## Objective

Modernize the complete visual interface of GRF Editor Safe without changing the GRF parsing, compression, encryption, validation, backup, or safe-save behavior. The application must remain familiar to experienced GRF Editor users while becoming clearer, more consistent, and easier to operate on current Windows displays.

## Approved Direction

The selected direction is **Classic refined, balanced layout (A2)**. It preserves the desktop-editor character of the existing WPF application rather than replacing it with a web-like or highly stylized interface.

The design uses a neutral light palette, restrained blue accents, compact controls, clear section boundaries, consistent spacing, and modernized icon treatment. Ragnarok references remain subtle and do not compete with file-management tasks.

## Scope

The visual update covers:

- the main editor window;
- menus, toolbars, search fields, tree, file list, preview area, splitters, tabs, progress, and status surfaces;
- settings, validation, safe-save reports, restore-backup flow, and commonly used operation dialogs;
- shared buttons, inputs, lists, tabs, group boxes, message surfaces, and window defaults;
- high-DPI behavior, keyboard focus visibility, disabled states, and text contrast;
- visual documentation and regression checks.

This work does not redesign specialist content renderers such as sprite, map, audio, hex, or image preview engines. Their surrounding chrome receives the shared theme, but their rendering logic remains unchanged.

## Non-goals and Safety Boundary

The visual layer must not modify:

- GRF header, table, entry, compression, encryption, or serialization code;
- safe-save transaction ordering, backup creation, validation, restore, or recovery logic;
- path compatibility, filename encoding, or archive format detection;
- the read-only treatment of unsupported or protected Event Horizon archives;
- original user GRFs or the established copy-first test workflow.

Existing command handlers and control names remain stable wherever possible. The update may recompose XAML layout, but it must bind to the same operations. No visual control may bypass the existing safe-save service.

## Architecture

### Shared theme layer

A new WPF resource dictionary in `GRFEditor/WPF/Styles` owns the application-specific design tokens and reusable control styles. `App.xaml` merges it after the legacy `GRFEditorStyles.xaml` dictionary so the modernized application can override selected presentation properties without changing TokeiLibrary or archive code.

Resources use semantic names for surfaces, borders, text, accents, success, warning, and danger states. Shared styles cover standard WPF controls first. Existing specialized Tokei controls receive narrowly scoped adapter styles only where required.

### Main window composition

`EditorMainWindow.xaml` retains its three-pane workflow:

1. folder navigation;
2. file list and filtering;
3. preview and inspection.

The top area is reorganized into a compact menu plus action bar. Open, safe save, undo/redo, navigation, validation, and search are visually prioritized. Advanced archive operations remain available through menus and are not promoted beside routine safe actions.

The safe-save state becomes a persistent, readable status strip with neutral, active, success, warning, and error variants. It communicates state through text and icon/color together; color alone is never the only signal.

Pane headings and counts clarify context without reducing working space. Splitters remain user-adjustable, and current persisted positions continue to load and save.

### Dialog consistency

Shared window and dialog resources provide consistent typography, padding, button order, minimum dimensions, headings, and message severity. Frequently used dialogs are updated first, followed by remaining application-owned XAML windows. Third-party controls and specialized editors retain their internal behavior.

Destructive or structurally risky archive actions use a distinct danger treatment and explicit language. Safe default actions receive primary emphasis. Cancel remains plainly available and keyboard accessible.

## Interaction and Attention Hierarchy

The hierarchy follows three levels:

- **Primary:** open archive, save safely, confirm safe dialog actions;
- **Secondary:** search, navigate, undo/redo, validate, extract, inspect;
- **Advanced:** compact, defragment, repack, encryption, and specialized tools.

This keeps the safest common workflow easiest to see and reduces accidental selection of advanced operations. It applies the relevant revenue-centric-design principles—clear attention hierarchy, good defaults, and feature discipline—without introducing monetization or promotional UI.

All existing keyboard shortcuts remain operational. Controls expose visible focus states. Tooltips explain icon-only actions, and important operations retain text labels. The design targets Windows scaling at 100%, 125%, 150%, and 200% without clipped labels or unreachable buttons.

## Visual System

- Typography: Segoe UI using Windows-native sizing, with restrained weight changes for hierarchy.
- Canvas: warm-neutral application background with white working surfaces.
- Accent: accessible medium blue for selection, focus, and primary actions.
- Status: distinct green, amber, and red supported by icons and text.
- Spacing: compact 4/8/12/16-pixel rhythm suitable for a dense desktop utility.
- Corners: subtle rounding only on buttons, inputs, cards, and status surfaces; data grids and panes remain crisp.
- Icons: consistent 16- and 20-pixel assets with clear silhouettes. Existing usable resources may remain; replacements must preserve command meaning.

The initial release is light-theme only. A dark theme is deliberately excluded to keep scope focused and avoid doubling visual regression work.

## Data and Control Flow

Visual controls continue to invoke the existing event handlers in `EditorMainWindow.xaml.cs`, `EMenuInteraction.cs`, and dialog code-behind. Theme resources affect presentation only.

Safe-save status is driven by the current application state. The visual layer formats that state but does not infer archive validity or save success. Errors continue to originate in the existing services and are displayed through consistent severity components.

## Error Handling

Missing optional icon resources must degrade to a text-labeled control rather than block startup. A missing theme resource discovered during development is treated as a build or startup defect and corrected before release.

Long-running operations retain progress and cancellation behavior. Error dialogs preserve the original technical details needed for diagnosis, while improving the heading, summary, and action layout.

Unsupported Event Horizon archives remain clearly identified as read-only. The visual refresh must not offer an enabled save path for them.

## Testing and Acceptance Criteria

### Automated checks

- Build the full solution in the supported Release configuration.
- Run all existing safe-save and compatibility tests unchanged.
- Add focused tests for any new presentation-state adapter that contains logic.
- Verify XAML resources load without missing keys or startup exceptions.
- Re-run the classic-GRF copy round trip and confirm the original archive hash remains unchanged.

### Visual checks

- Inspect the main window and representative dialogs at 100%, 125%, 150%, and 200% Windows scaling.
- Verify minimum window sizes, splitter movement, long paths, empty states, progress states, validation warnings, and safe-save outcomes.
- Verify keyboard-only navigation, shortcut preservation, visible focus, contrast, and disabled-state clarity.
- Compare screenshots for alignment, clipping, accidental legacy styling, and inconsistent spacing.

### Release acceptance

The update is accepted when the complete application-owned interface follows the classic refined system, common workflows remain familiar, no archive behavior changes, all compatibility tests pass, and a real copied classic GRF completes the established safe-save validation while its original remains byte-for-byte unchanged.

## Delivery Strategy

Implementation proceeds in reviewable increments:

1. shared tokens and control styles;
2. main window shell and status hierarchy;
3. common dialogs and safe-save surfaces;
4. remaining application-owned windows;
5. DPI, accessibility, visual regression, compatibility testing, documentation, and packaged side-by-side release.

Each increment must build and preserve the archive test suite. The final executable remains **GRF Editor Safe**, installed and launched side by side with the original GRF Editor.
