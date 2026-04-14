# ✨🧰 No More Pain — Unity Editor Tools 🧰✨

> Набор инструментов для повышения продуктивности в Unity Editor 🚀  
> A set of productivity tools for the Unity Editor 🚀

---

## 🇷🇺 Русский 🇷🇺

### 🌟 Быстрый взгляд 🌟

- 🎯 Быстрая навигация по папкам в Hierarchy (Folder Navbar)
- 🎨 Цветные папки и строки в Project (Folder Colors / Row Colors)
- 👀 Hover Preview по `Alt` для объектов с мешем
- 📌 Удобный Inspector workflow: Tabs, Copy/Paste, Save в Play Mode

### 🚀 Возможности 🚀

#### 🧩 Иерархия — Auto GameObject Icon
Заменяет стандартный куб на иконку основного компонента объекта.  
Приоритет: кастомная иконка → коллайдер → первый компонент.

#### 🧱 Иерархия — Иконки компонентов справа
Справа в строке объекта отображаются иконки компонентов.  
Нажмите на иконку — откроется всплывающее окно с инспектором этого компонента.

#### 🏷️ Иерархия — Бейджи Tag / Layer
Показывает нестандартные теги и слои в виде цветных бейджей прямо в иерархии.

#### 🦓 Иерархия — Зебра
Чередующаяся подсветка строк для удобного визуального разделения.

#### 🌲 Иерархия — Линии дерева
Рисует линии связей между родителями и дочерними объектами.

#### ✅ Иерархия — Тогл активности
Чекбокс включения/выключения объекта появляется при наведении на строку.

#### 🎨 Иерархия — Подсветка цветом
Выделяет строки объектов полупрозрачным цветом с акцентной полосой слева.  
- **Alt + ЛКМ** по объекту — открыть палитру цветов  
- **ПКМ → Highlight Color...** — то же через контекстное меню  
- Данные сохраняются в `ProjectSettings/NoMorePainColors.json`

#### 📁 Иерархия — Папки
Позволяет создавать визуальные папки в иерархии.  
- **ПКМ → Create Folder** — создать папку  
- **ПКМ → Mark as Folder / Unmark as Folder** — пометить/снять метку папки у объекта  
- Данные сохраняются в `ProjectSettings/NoMorePainFolders.json`

#### 🧭 Иерархия — Folder Navbar
Панель папок в строке поиска иерархии для быстрого перехода между папками-секциями.  
Включает кнопку поиска и адаптивное поведение при узкой ширине окна.

#### 👀 Иерархия — Hover Preview (Alt)
При зажатом **Alt** и наведении на объект с мешем показывает всплывающее превью.  
- Превью показывается только для объекта под курсором (без дочерних)  
- Используется адаптивный fit, чтобы модель помещалась в окно с отступами

#### 🗂️ Project — Folder Style
Стилизация папок в окне Project (левая и правая панели):  
- Цвет иконки папки (Folder Colors)  
- Подсветка строк в левой панели (Row Colors)  
- Badge-иконка в правом нижнем углу папки (авто или кастомная)  
- Линии дерева в левой панели (Tree Lines)  
- Zebra striping в левой панели (Zebra Striping)

Настройка папки:  
- **Assets → No More Pain → Folder Style...**  
- **Alt + ЛКМ** по папке в окне Project

Данные сохраняются в `ProjectSettings/NoMorePainProjectFolderStyles.json`.

#### 📌 Табы инспектора
Полоса закреплённых объектов в верхней части Inspector. Табы хранятся отдельно для каждой сцены.  
- **Add Tab** — закрепить текущий объект  
- **Remove** — открепить текущий объект  
- Drag-and-drop объекта на полосу  
- Кнопки **< >** для навигации между табами  
- **ПКМ** по табу — удалить или пинговать объект  
- Данные сохраняются в `ProjectSettings/NoMorePainTabs.json`

#### 📋 Копирование / вставка компонентов
Пакетное копирование и вставка компонентов между объектами.  
- **Copy** — открыть список компонентов с чекбоксами  
- **Paste (N)** — вставить выбранные компоненты на все выделенные объекты  
  - Если компонент уже есть — перезаписываются значения  
  - Если нет — компонент добавляется  
- **✕** — очистить буфер  
- Полная поддержка Undo

#### 💾 Сохранение в Play Mode
Сохраняет значения компонентов в Play Mode и восстанавливает после выхода.  
- **Save** в заголовке Inspector (только в Play Mode) — зафиксировать значения  
- **✕** — отменить снимок  
- Восстановление с поддержкой Undo

---

### ⚙️ Настройки ⚙️

Все функции можно включать и выключать по отдельности:  
`Tools → No More Pain → Settings`

💡 Подсказка: если интерфейс кажется перегруженным, отключите ненужные блоки точечно в настройках.

---

### 📦 Установка 📦

**Через UPM (Package Manager):**

1. Откройте `Window → Package Manager`
2. Нажмите **+** → `Add package from git URL...`
3. Введите:
```
https://github.com/M-A-L-bl-LLl/NoMorePainEditor.git
```

**Или вручную в `Packages/manifest.json`:**
```json
{
  "dependencies": {
    "com.nomorepain.editor": "https://github.com/M-A-L-bl-LLl/NoMorePainEditor.git"
  }
}
```

**Через скачивание релиза:**

1. Перейдите на страницу [Releases](https://github.com/M-A-L-bl-LLl/NoMorePainEditor/releases) и скачайте архив последней версии
2. Распакуйте архив в любую папку
3. Откройте `Window → Package Manager`
4. Нажмите **+** → `Add package from disk...`
5. Укажите `package.json` внутри распакованной папки

**Требования:** Unity 2021.3+

---

## 🇬🇧 English 🇬🇧

### 🌟 Quick Look 🌟

- 🎯 Fast folder navigation in Hierarchy (Folder Navbar)
- 🎨 Colored folders and rows in Project (Folder Colors / Row Colors)
- 👀 `Alt` hover preview for mesh objects
- 📌 Smooth Inspector workflow: Tabs, Copy/Paste, Save in Play Mode

### 🚀 Features 🚀

#### 🧩 Hierarchy — Auto GameObject Icon
Replaces the default cube icon with the primary component icon.  
Priority: custom icon → collider → first component.

#### 🧱 Hierarchy — Right Component Icons
Displays component icons on the right side of each hierarchy row.  
Click an icon to open a floating quick inspector for that component.

#### 🏷️ Hierarchy — Tag / Layer Badges
Shows non-default tags and layers as colored badges directly in the Hierarchy.

#### 🦓 Hierarchy — Zebra Striping
Alternating row tint for easier visual scanning.

#### 🌲 Hierarchy — Tree Lines
Draws parent-child connection lines.

#### ✅ Hierarchy — Active Toggle
Shows an enable/disable checkbox when hovering a row.

#### 🎨 Hierarchy — Row Colors
Highlights hierarchy rows with a semi-transparent color and left accent stripe.  
- **Alt + LMB** on an object — open color picker  
- **Right-click → Highlight Color...** — same via context menu  
- Data is saved to `ProjectSettings/NoMorePainColors.json`

#### 📁 Hierarchy — Folders
Creates visual folders in the hierarchy.  
- **Right-click → Create Folder**  
- **Right-click → Mark as Folder / Unmark as Folder**  
- Data is saved to `ProjectSettings/NoMorePainFolders.json`

#### 🧭 Hierarchy — Folder Navbar
Folder navigation bar in the hierarchy search area for quick jumps between folder sections.  
Includes search button integration and adaptive behavior in narrow layouts.

#### 👀 Hierarchy — Hover Preview (Alt)
While holding **Alt** and hovering a hierarchy object with a mesh, shows a popup preview.  
- Preview is shown only for the hovered object (not children)  
- Adaptive fit keeps the model inside the preview window with padding

#### 🗂️ Project — Folder Style
Folder styling in the Project window (left and right panes):  
- Folder icon color (Folder Colors)  
- Left-pane row tinting (Row Colors)  
- Bottom-right badge icon on folder (auto or custom)  
- Left-pane tree lines (Tree Lines)  
- Left-pane zebra striping (Zebra Striping)

Configure folder style via:  
- **Assets → No More Pain → Folder Style...**  
- **Alt + LMB** on a folder in Project window

Data is saved to `ProjectSettings/NoMorePainProjectFolderStyles.json`.

#### 📌 Inspector Tabs
Pinned objects strip at the top of Inspector. Tabs are stored per scene.  
- **Add Tab** — pin current object  
- **Remove** — unpin current object  
- Drag-and-drop objects onto the strip  
- **< >** navigation buttons  
- **Right-click** a tab to remove or ping object  
- Data is saved to `ProjectSettings/NoMorePainTabs.json`

#### 📋 Component Copy / Paste
Batch copy-paste of components between GameObjects.  
- **Copy** — opens component picker with checkboxes  
- **Paste (N)** — pastes selected components to all selected objects  
  - Existing component: overwrite values  
  - Missing component: add component  
- **✕** — clear clipboard  
- Full Undo support

#### 💾 Play Mode Save
Captures component values in Play Mode and restores them after exiting Play Mode.  
- **Save** button in Inspector header (Play Mode only)  
- **✕** button to discard snapshot  
- Restore with full Undo support

---

### ⚙️ Settings ⚙️

Each feature can be toggled independently:  
`Tools → No More Pain → Settings`

💡 Tip: if the UI feels too busy, disable specific modules in Settings.

---

### 📦 Installation 📦

**Via UPM (Package Manager):**

1. Open `Window → Package Manager`
2. Click **+** → `Add package from git URL...`
3. Enter:
```
https://github.com/M-A-L-bl-LLl/NoMorePainEditor.git
```

**Or manually in `Packages/manifest.json`:**
```json
{
  "dependencies": {
    "com.nomorepain.editor": "https://github.com/M-A-L-bl-LLl/NoMorePainEditor.git"
  }
}
```

**Via release download:**

1. Go to [Releases](https://github.com/M-A-L-bl-LLl/NoMorePainEditor/releases) and download the latest archive
2. Extract the archive to any local folder
3. Open `Window → Package Manager`
4. Click **+** → `Add package from disk...`
5. Select `package.json` inside the extracted folder

**Requirements:** Unity 2021.3+

---

### 📄 License 📄

MIT License — see [LICENSE](LICENSE) for details.
