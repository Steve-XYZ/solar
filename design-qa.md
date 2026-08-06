# Design QA

**Source visual truth**

- Path: `C:\Users\yandy\.codex\generated_images\019fd30f-c151-7291-8d2d-dc596096d7cf\exec-944d547e-2d67-45a5-b2f0-ff655eb0a5b8.png`
- Pixels: 853 × 1856.
- Intended app viewport: 390 × 844 portrait, without device chrome.
- State: automatic BLE scan with a discovered `PC-*` device.

**Rendered implementation**

- Screenshot path: unavailable.
- Intended viewport: 390 × 844 portrait on Android API 26 or newer.
- Density normalization: not performed because no implementation capture was available.
- State: `DevicesPage` automatic scanning state.

**Findings**

- [P1] Native implementation capture is unavailable.
  Location: Android runtime validation.
  Evidence: `adb devices -l` returned no attached device. The local Android SDK contains build tools and platform tools but no emulator or system image; `sdkmanager` could not fetch Google package manifests to install them.
  Impact: typography, clipping, elevation, icon rendering and 390 × 844 spacing cannot be compared visually against the selected design.
  Fix: install the generated APK on a physical Android device or an emulator, capture Conexión at 390 × 844-equivalent dimensions, and compare it with the source image in one combined visual input.

**Required fidelity surfaces**

- Fonts and typography: Open Sans and Font Awesome resources compile successfully; rendered weight, wrapping and antialiasing remain unverified.
- Spacing and layout rhythm: both main pages use fixed, non-scrollable native grids; rendered sizing remains unverified.
- Colors and visual tokens: the Orbit Mint palette is implemented as shared resources; rendered color and contrast remain unverified.
- Image quality and asset fidelity: the generated solar/battery brand mark is bundled; runtime scaling and background blending remain unverified.
- Copy and content: Spanish connection, telemetry and menu copy is present and compiles; truncation remains unverified.

**Primary interactions checked**

- Static/code validation only: automatic scan lifecycle, device-card connect command, connection transition, two primary Shell destinations, overflow routes and disconnect path.
- Browser console: not applicable to the native MAUI application.
- Android runtime interaction: blocked because no device or emulator is available.

**Full-view comparison evidence**

- Blocked: no rendered app capture exists.

**Focused-region comparison evidence**

- Blocked for the same reason; header, detector waves, device card and overflow menu could not be captured.

**Comparison history**

- No visual iteration could start without a rendered implementation artifact.

**Implementation checklist**

1. Install the APK on a physical Android device.
2. Capture Conexión while scanning and with the overflow menu open.
3. Connect to the physical BMS and capture Resumen after telemetry arrives.
4. Compare both captures against the selected visual direction and fix any P0/P1/P2 drift.

**Follow-up polish**

- Evaluate the radar cadence and transition timing on a 60 Hz physical display.
- Confirm that the generated brand mark remains crisp at the 46 dp header size.

final result: blocked
