# No More Pain — Unity Editor Tools

> Набор инструментов для повышения продуктивности в Unity Editor.  
> A set of productivity tools for the Unity Editor.

---

## 🇷🇺 Русский

### Возможности

#### Иерархия — иконки компонентов
Заменяет стандартный куб на иконку основного компонента объекта.  
Приоритет: кастомная иконка → коллайдер → первый компонент.  
Справа отображаются иконки всех остальных компонентов — нажмите на любую, чтобы открыть окно быстрого редактирования.

#### Иерархия — подсветка цветом
Выделяет строки объектов полупрозрачным цветом с акцентной полосой слева.  
- **Alt + ЛКМ** по объекту — открыть палитру цветов  
- **ПКМ → Highlight Color...** — то же через контекстное меню  
- Поддержка кастомного цвета и выбора иконки из всех компонентов Unity  
- Данные сохраняются в `ProjectSettings/NoMorePainColors.json`

#### Иерархия — папки
Создаёт папки в иерархии.  
- **ПКМ → Create Folder** — создать папку  
- **ПКМ → Mark as Folder / Unmark as Folder** — пометить/убрать метку с любого объекта  
- Данные сохраняются в `ProjectSettings/NoMorePainFolders.json`

#### Иерархия — линии дерева
Рисует линии, соединяющие родительские и дочерние объекты.

#### Табы инспектора
Полоска закреплённых объектов в верхней части инспектора.  
- Нажмите **+** чтобы закрепить текущий объект  
- Перетащите любой объект на полоску  
- Кнопки **< >** для навигации  
- Данные сохраняются в `ProjectSettings/NoMorePainTabs.json`

#### Быстрое редактирование компонента
Нажмите на иконку компонента справа в иерархии — откроется всплывающее окно с полным инспектором компонента и тоглом включения/выключения.

#### Копирование/вставка компонентов
Копирует значения полей компонента и вставляет их в компонент того же типа на другом объекте.

#### Сохранение в Play Mode
Сохраняет выбранные значения компонентов между входом и выходом из Play Mode.

---

### Установка

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

**Требования:** Unity 2021.3+

---

---

## 🇬🇧 English

### Features

#### Hierarchy — Component Icons
Replaces the default cube with the primary component icon.  
Priority: custom icon → collider → first component.  
Additional component icons are shown on the right — click any to open a quick-edit window.

#### Hierarchy — Color Highlight
Highlights object rows with a semi-transparent color and a solid accent stripe on the left.  
- **Alt + LMB** on any object — open the color picker  
- **Right-click → Highlight Color...** — same via context menu  
- Supports custom colors and icon selection from all Unity components  
- Data is saved to `ProjectSettings/NoMorePainColors.json`

#### Hierarchy — Folders
Creates folders in the hierarchy.  
- **Right-click → Create Folder** — create a new folder  
- **Right-click → Mark as Folder / Unmark as Folder** — tag any existing object as a folder  
- Data is saved to `ProjectSettings/NoMorePainFolders.json`

#### Hierarchy — Tree Lines
Draws lines connecting parent and child objects.

#### Inspector Tabs
A pinned-objects strip at the top of the Inspector.  
- Click **+** to pin the current object  
- Drag any object onto the strip  
- Use **< >** buttons to navigate between pinned objects   
- Data is saved to `ProjectSettings/NoMorePainTabs.json`

#### Component Quick Edit
Click a component icon in the Hierarchy to open a floating inspector for that component with an enable/disable toggle.

#### Component Copy / Paste
Copy field values from one component and paste them into a component of the same type on another object.

#### Play Mode Save
Preserves selected component values when exiting Play Mode.

---

### Installation

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

**Requirements:** Unity 2021.3+

---

### License

MIT License — see [LICENSE](LICENSE) for details.
