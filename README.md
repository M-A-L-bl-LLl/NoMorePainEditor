# No More Pain — Unity Editor Tools

> Набор инструментов для повышения продуктивности в Unity Editor.  
> A set of productivity tools for the Unity Editor.

---

## 🇷🇺 Русский

### Возможности

#### Иерархия — Auto GameObject Icon
Заменяет стандартный куб на иконку основного компонента объекта.  
Приоритет: кастомная иконка → коллайдер → первый компонент.

#### Иерархия — Иконки компонентов справа
Справа в строке объекта отображаются иконки всех компонентов.  
Нажмите на любую — откроется всплывающее окно с полным инспектором компонента и тоглом включения/выключения.

#### Иерархия — Бейджи Tag / Layer
Показывает нестандартный тег и слой объекта в виде цветных бейджей прямо в иерархии.

#### Иерархия — Зебра
Чередующаяся подсветка строк для удобного визуального разделения объектов.

#### Иерархия — Линии дерева
Рисует линии, соединяющие родительские и дочерние объекты.

#### Иерархия — Тогл активности
Чекбокс включения/выключения объекта появляется при наведении курсора на строку в иерархии.

#### Иерархия — Подсветка цветом
Выделяет строки объектов полупрозрачным цветом с акцентной полосой слева.  
- **Alt + ЛКМ** по объекту — открыть палитру цветов  
- **ПКМ → Highlight Color...** — то же через контекстное меню  
- Данные сохраняются в `ProjectSettings/NoMorePainColors.json`

#### Иерархия — Папки
Создаёт визуальные папки в иерархии.  
- **ПКМ → Create Folder** — создать папку  
- **ПКМ → Mark as Folder / Unmark as Folder** — пометить/убрать метку с любого объекта  
- Данные сохраняются в `ProjectSettings/NoMorePainFolders.json`

#### Табы инспектора
Полоска закреплённых объектов в верхней части инспектора. Табы сохраняются отдельно для каждой сцены.  
- Кнопка **Add Tab** — закрепить текущий объект  
- Кнопка **Remove** — открепить текущий объект  
- Перетащите любой объект на полоску  
- Кнопки **< >** для навигации между табами  
- **ПКМ** по табу — удалить или пинговать объект  
- Данные сохраняются в `ProjectSettings/NoMorePainTabs.json`

#### Копирование/вставка компонентов
Пакетное копирование и вставка компонентов между объектами.  
- Кнопка **Copy** — открывает список компонентов объекта с чекбоксами для выбора  
- Кнопка **Paste (N)** — вставляет скопированные компоненты на все выделенные объекты  
  - Если компонент уже есть — перезаписывает значения  
  - Если нет — добавляет новый  
- Кнопка **✕** — очищает буфер обмена  
- Полная поддержка Undo

#### Сохранение в Play Mode
Сохраняет значения компонентов в Play Mode и восстанавливает их после выхода.  
- Кнопка **Save** в заголовке инспектора (только в Play Mode) — зафиксировать текущие значения  
- Кнопка **✕** — отменить снимок  
- Восстановление с поддержкой Undo

---

### Настройки

Все функции можно включать и выключать по отдельности:  
`Tools → No More Pain → Settings`

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

**Через скачивание релиза:**

1. Перейдите на страницу [Releases](https://github.com/M-A-L-bl-LLl/NoMorePainEditor/releases) и скачайте архив последней версии
2. Распакуйте архив в любую папку на вашем компьютере
3. Откройте `Window → Package Manager`
4. Нажмите **+** → `Add package from disk...`
5. Укажите путь до файла `package.json` внутри распакованной папки

**Требования:** Unity 2021.3+

---

---

## 🇬🇧 English

### Features

#### Hierarchy — Auto GameObject Icon
Replaces the default cube with the primary component icon.  
Priority: custom icon → collider → first component.

#### Hierarchy — Right Component Icons
Component icons are shown on the right side of each hierarchy row.  
Click any icon to open a floating inspector for that component with an enable/disable toggle.

#### Hierarchy — Tag / Layer Badges
Shows non-default tags and layers as colored badges directly in the Hierarchy.

#### Hierarchy — Zebra Striping
Alternating row tint for easier visual scanning.

#### Hierarchy — Tree Lines
Draws lines connecting parent and child objects.

#### Hierarchy — Active Toggle
An enable/disable checkbox appears on hover for each object row.

#### Hierarchy — Row Colors
Highlights object rows with a semi-transparent color and a solid accent stripe on the left.  
- **Alt + LMB** on any object — open the color picker  
- **Right-click → Highlight Color...** — same via context menu  
- Data is saved to `ProjectSettings/NoMorePainColors.json`

#### Hierarchy — Folders
Creates visual folders in the hierarchy.  
- **Right-click → Create Folder** — create a new folder  
- **Right-click → Mark as Folder / Unmark as Folder** — tag any existing object as a folder  
- Data is saved to `ProjectSettings/NoMorePainFolders.json`

#### Inspector Tabs
A pinned-objects strip at the top of the Inspector. Tabs are saved per scene.  
- **Add Tab** button — pin the current object  
- **Remove** button — unpin the current object  
- Drag any object onto the strip  
- **< >** buttons to navigate between tabs  
- **Right-click** a tab — remove or ping the object  
- Data is saved to `ProjectSettings/NoMorePainTabs.json`

#### Component Copy / Paste
Batch copy and paste components between GameObjects.  
- **Copy** button — opens a component picker with checkboxes  
- **Paste (N)** button — pastes all copied components to every selected object  
  - Existing component: overwrites values  
  - Missing component: adds it  
- **✕** button — clears the clipboard  
- Full Undo support

#### Play Mode Save
Captures component values during Play Mode and restores them on exit.  
- **Save** button in the Inspector header (Play Mode only) — capture current values  
- **✕** button — discard the snapshot  
- Restore with full Undo support

---

### Settings

Each feature can be toggled independently:  
`Tools → No More Pain → Settings`

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

**Via release download:**

1. Go to [Releases](https://github.com/M-A-L-bl-LLl/NoMorePainEditor/releases) and download the latest archive
2. Extract the archive to any folder on your machine
3. Open `Window → Package Manager`
4. Click **+** → `Add package from disk...`
5. Select the `package.json` file inside the extracted folder

**Requirements:** Unity 2021.3+

---

### License

MIT License — see [LICENSE](LICENSE) for details.
