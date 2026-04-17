
# 🧠 AGENT.md — Edumy VR Learning System

## 1. 🎯 Project Overview

This is a **Unity-based VR learning platform** integrated with a backend course system (Node.js + MongoDB).

The system supports:

* Course browsing
* Lesson selection
* Video playback (stream resolved)
* Slides (world-space UI)
* Quiz (normal + timed)
* Spatial XR windows (drag / resize / pin)

⚠️ **IMPORTANT RULE**
Do NOT break:

* Spatial window system
* CourseSelection flow
* API compatibility layer

---

## 2. 🏗️ Architecture Overview

### Core Flow

```
CourseSelectionUI (MAIN ORCHESTRATOR)
        ↓
Load Courses (ApiManager)
        ↓
User selects Lesson
        ↓
Route by type:
    → Slide → SlidePopupWindow
    → Quiz → QuizPopupWindow / TimedQuizPopupWindow
    → Video → VideoPopupWindow
```

---

## 3. 📦 Modules Breakdown

---

### 🔹 CORE (API + Models + Helpers)

#### `ApiManager.cs`

* Singleton REST client

* JWT priority:

  1. ENV: `EDUMY_VR_JWT_TOKEN`
  2. PlayerPrefs
  3. Hardcoded fallback

* Responsibilities:

  * Fetch courses
  * Fetch sections/lessons
  * Update progress
  * Resolve video stream URL

⚠️ MUST KEEP:

* Fallback endpoints (3 candidates)
* Flexible JSON parsing (backend schema may change)

---

#### `DataModels.cs`

Defines:

* `CourseData`
* `LessonData`
* `SectionData`
* `QuizQuestionData`
* `TimedQuizData`
* `StreamResolveResponse`

⚠️ Rule:

* NEVER rename fields unless backend updated

---

#### `UiScreenState.cs`

Enum:

```
LessonSelection
Slide
Quiz
Video
```

---

#### `XRRuntimeUiHelper.cs`

* XR-compatible UI Toolkit setup
* Input + interaction bridge

---

### 🔹 COURSE SELECTION (CRITICAL MODULE)

#### `CourseSelectionUI.cs` ⚠️ (VERY IMPORTANT)

* ~1674 lines (central brain)
* Handles:

  * UI build
  * API calls
  * Navigation logic
  * Fallback mock data

#### Routing Logic:

```
IF slide exists → open Slide
ELSE IF quiz → open Quiz
ELSE → open Video
```

#### Also handles:

* Settings panel
* Video mode toggle (dock ↔ float)

⚠️ DO NOT:

* Split logic randomly
* Change routing priority
* Break mock fallback

---

#### UI Elements:

* `CourseCardElement.cs`
* `LessonItemElement.cs`
* `SectionItemElement.cs`

---

#### Controllers:

* `MainScreenController.cs`
* `LessonSelectionController.cs`
* `CourseToggleController.cs`

---

### 🔹 VIDEO SYSTEM

#### `VideoPopupWindow.cs`

* World-space video window
* Uses `RenderTexture`
* Handles:

  * URL normalization
  * YouTube detection (watch/shorts)
  * Fallback handling

⚠️ MUST KEEP:

* Cleanup in `OnDestroy`
* RenderTexture lifecycle

---

#### `VideoControlsOverlay.cs`

* Play / Pause
* Seek
* Volume
* Speed

---

#### `VideoWindowModeController.cs`

* Toggle:

  * Docked screen
  * Floating VR window

---

### 🔹 QUIZ SYSTEM

#### Main:

* `QuizPopupWindow.cs`
* `QuizQuestionView.cs`

#### Timed Quiz:

* `TimedQuizPopupWindow.cs`
* `VideoQuizScheduler.cs`

Supports:

* Time formats:

  * seconds
  * mm:ss
  * hh:mm:ss

⚠️ MUST SUPPORT:

* VideoPlayer time
* External time provider (YouTube bridge)

---

#### UI Components:

* `AnswerCard.cs`
* `QuizPopupPanel.cs`
* `QuizController.cs`

---

### 🔹 SLIDE SYSTEM

#### Main:

* `SlidePopupWindow.cs`
* `SlideViewer.cs`

Features:

* World-space rendering
* Prev / Next navigation

---

#### Supporting:

* `SlidePopupPanel.cs`
* `SlideController.cs`

---

### 🔹 SPATIAL / XR INTERACTION (CRITICAL)

#### `SpatialWindow.cs`

Core system:

* Drag
* Resize
* Pin / Unpin
* Follow user when unpinned

---

#### `SpatialWindowHandle.cs`

* Extends `XRBaseInteractable`

📍 Important:

* Line 62 = `OnSelectEntered`
* Calls:

```
owner.OnHandleSelectEntered(...)
```

⚠️ DO NOT BREAK:

* selectEntered / selectExited events

---

#### `VRPanelAnchorManager.cs`

* Handles:

  * Browsing mode
  * Video mode
  * Anchor positioning (camera/right hand)
  * FOV clamping

---

#### `VRSimulatorBootstrap.cs`

* XR initialization
* Camera + hand binding

---

## 4. 🎮 Scenes & Assets

### Main Scene

* `VRCourseSelection.unity`

### Other Scenes

* `BasicScene.unity`
* `SampleScene.unity`

---

### Prefabs

* `VRUI` (MAIN UI ROOT)

---

### UI Toolkit

Located in:

```
VRCourseSelection/
```

Includes:

* UXML
* USS

---

## 5. ⚙️ Tech Stack

### XR

* OpenXR
* XR Interaction Toolkit
* XR Hands
* XR Management
* AR Foundation

---

### Rendering

* URP

---

### Input

* Unity Input System

---

### Backend

* Node.js (external)
* MongoDB

---

### Other

* AI packages (installed but optional)

---

## 6. 🚨 Critical Rules for AI Agents

### ❌ DO NOT:

* Merge all windows into 1 UI
* Break SpatialWindow system
* Remove fallback API logic
* Change lesson routing priority
* Rename DataModels fields

---

### ✅ ALWAYS:

* Keep windows separated:

  * CourseSelection
  * Video
  * Quiz
  * Slide

* Maintain:

  * World-space UI
  * XR interaction
  * Modular popup system

---

### 🧩 When Adding Features

Follow this pattern:

```
1. Add new UI → Popup Window
2. Register in CourseSelectionUI
3. Route by lesson type
4. Keep SpatialWindow compatibility
```

---

## 7. 🧪 Editor vs Runtime Behavior

| Mode    | Behavior                    |
| ------- | --------------------------- |
| Editor  | Uses mock data if API fails |
| Runtime | Uses real API               |

⚠️ Agent must NOT remove mock system

---

## 8. 🧠 Common Pitfalls

* Breaking drag/resize → caused by collider or XR interaction conflict
* Video not rendering → RenderTexture mismanagement
* Quiz timing wrong → incorrect time parsing
* API crash → schema mismatch → MUST use flexible parsing

---

## 9. 📌 Suggested Improvements (Safe)

AI Agent MAY:

* Improve UI Toolkit styling (USS)
* Refactor into smaller methods (NOT logic)
* Add logging/debug tools
* Optimize performance

AI Agent MUST NOT:

* Rewrite architecture
* Replace XR system
* Change backend contract

---

## 10. 🧭 Mental Model

Think of system as:

```
Backend (Courses API)
        ↓
CourseSelectionUI (Brain)
        ↓
Popup Windows (Video / Quiz / Slide)
        ↓
Spatial XR System (Interaction Layer)
```

---

## ✅ Final Note

This project is:

> A hybrid between **Udemy-like LMS + VR Spatial UI system**

Any modification must preserve:

* Learning flow
* XR interaction
* Modular popup architecture
