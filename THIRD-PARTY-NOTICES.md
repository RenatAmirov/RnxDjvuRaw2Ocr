# Сторонние компоненты

Приложение запускает OCR-утилиты отдельными процессами. Исходники сторонних компонентов в этот репозиторий не скопированы. Скрипты подготовки получают бинарные файлы и модели из перечисленных источников; скрипт публикации сохраняет upstream-документацию и лицензии рядом с ними.

| Компонент | Источник | Лицензия |
| --- | --- | --- |
| DjVuLibre 3.5.29 / DjView 4.12, Windows build | https://sourceforge.net/projects/djvu/files/DjVuLibre_Windows/3.5.29%2B4.12/ | GPL; полный текст в `tools/djvulibre/COPYING.txt` |
| Исходники DjVuLibre 3.5.29 | https://sourceforge.net/projects/djvu/files/DjVuLibre/3.5.29/ | См. COPYING/COPYRIGHT upstream |
| Tesseract OCR 5.5.3, Windows build | https://github.com/tesseract-ocr/tesseract/releases/tag/5.5.3 | Apache-2.0 и лицензии входящих библиотек; см. `tools/tesseract/doc` |
| Русская и английская модели tessdata_fast 4.1.0 | https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0 | Apache-2.0, `tools/tesseract/tessdata/LICENSE-models.txt` |
| 7-Zip 26.06, только для распаковки при подготовке | https://github.com/ip7z/7zip/releases/tag/26.06 | LGPL/BSD и ограничение unRAR; https://www.7-zip.org/license.txt |
| Microsoft Visual C++ runtime x86 14.0.33321.0 (`msvcp140.dll`, `vcruntime140.dll`) | https://download.microsoft.com/download/4/7/c/47c6134b-d61f-4024-83bd-b9c9ea951c25/Microsoft.VCLibs.x86.14.00.Desktop.appx | Microsoft redistributable runtime; https://learn.microsoft.com/en-us/cpp/windows/deployment-in-visual-cpp |
| .NET / Windows App SDK / WinUI | https://github.com/dotnet/runtime и https://github.com/microsoft/WindowsAppSDK | Лицензии соответствующих NuGet-пакетов и runtime |

При распространении bundled-сборки DjVuLibre необходимо выполнять условия GPL, включая предоставление соответствующего исходного кода способом, допускаемым лицензией. Полный состав и лицензии транзитивных нативных библиотек определяются поставкой upstream. Этот файл не заменяет их тексты лицензий.
