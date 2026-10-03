# Tools

Python scripts that regenerate checked-in assets. Run them from the repo root.

`ui/` holds PowerShell scripts for driving the running app: screenshots, clicks, typing and the tray menu. See [`ui/README.md`](ui/README.md).

| Script | Writes | Needs |
|---|---|---|
| `extract-icons.py` | `src/WslcDesktop.App/Icons/IconData.g.cs` | `ref/MewUI` cloned |
| `make-icon.py` | `src/WslcDesktop.App/Assets/app.ico` | Pillow (`pip install pillow`) |

## extract-icons.py

`IconData.g.cs` holds the Fluent UI System Icons (MIT) the app uses, as path-geometry strings taken from MewUI's Gallery (`ref/MewUI/samples/MewUI.Gallery/Resources/Icons.xaml`).

To add an icon, add its name (for example `"pin_regular"`) to the `ICONS` list in the script, then run:

```powershell
python tools/extract-icons.py
```

The constant is named in PascalCase without the `_regular` suffix (`pin_regular` → `IconData.Pin`).

## make-icon.py

Draws the app icon (a cube on a rounded blue tile) at 16–256 px and saves it as a multi-size `.ico`:

```powershell
python tools/make-icon.py
```
