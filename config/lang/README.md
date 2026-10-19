# Language files

`de.json` = German texts of the web UI, config help and browser tiles. It is copied to `lang/` next to the bot (next to `settings.json` and `ogamed`).

Format (one file per language, file name = language code):

- `name` - name shown in the language selector
- `ui.exact` - English text -> translated text (menu, pages, labels, messages); `ui.phrases` and `ui.rx` are for generated labels and numbers
- `help` - help texts of the config page, same structure as the English help (`block -> option -> {t, e}`); missing entries stay English
- `tiles` - texts of the browser tiles (English text -> translated text)

Add a language: copy `de.json` to `lang/<code>.json`, change `name` and translate the values. It then appears in the language selector. Changes need no rebuild; reload the page. Text without a translation stays English.
