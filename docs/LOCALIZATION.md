# Languages and language packs

Introduced in source **1.28.0**, the nine built-in UI languages are English, 한국어, 日本語, 简体中文, 繁體中文, Español, Français, Deutsch and Português (Brasil). Current source **1.31.3** contains **353 messages** per language, including temperature status alongside switching, continuation, quota/history and security messages. Published **1.27.6** binaries still provide English/Korean; build current source until a new binary release is published.

Select the language code in the title bar, then choose a language by its native name. Selection updates the Windows/Mac main UI, menus and history without restarting account services. The preference is stored with existing settings; authored account names, chat IDs, models/permissions and credentials are not translated or migrated.

## Import and restore

Choose **Import language pack…** in the language menu to select a local UTF-8 JSON file. Only text is imported; there are no scripts, binaries, plugins or remote downloads. Choose a source you trust because translations can change labels and safety messages. Packs are stored privately in the existing application data directory's `languages/` subfolder as `pack-<code>.json`; the prefix avoids Windows reserved device names without excluding valid language tags.

A local pack can add a language or override a built-in translation. Missing messages use English, and the menu tooltip reports provided/total coverage. This coverage counts structurally supplied messages, not approval by native speakers. **Restore built-in language pack** removes the active local override; a language with no built-in original returns to English. Other packs and account data remain intact.

If an installed pack is corrupt, it is rejected and reported; other valid/built-in packs remain usable. If the saved pack is absent, English is displayed while the saved preference is preserved. There is no silent plaintext credential fallback: language-pack storage is independent of the encrypted credential vault.

## Contribute a translation

The shared [pack index](../localization/index.json) defines shipped languages; [English](../localization/packs/en.json) is the reference. Message IDs such as `msg.add_account` are stable and based on English. Existing Korean source-call aliases live separately in [legacy mappings](../localization/legacy-keys.json); translators do not need to edit them or know Korean.

Create a template:

```text
python scripts/new-language-pack.py --code ca --native-name "Català" --english-name "Catalan" --culture ca-ES --output artifacts/ca.json
```

Copy only the IDs you have translated into `strings`, using the English reference. Translate at least one message before importing. Missing entries intentionally show English; do not copy every English value just to claim completion. To ship a built-in pack, translate every message, add its code to the index and submit a focused issue-linked PR with actual program screenshots and validation. A new language needs no executable changes. [JSON schema](../localization/schema.json)

```json
{
  "schemaVersion": 1,
  "code": "ca",
  "nativeName": "Català",
  "englishName": "Catalan",
  "culture": "ca-ES",
  "direction": "ltr",
  "strings": { "msg.add_account": "Afegeix un compte" }
}
```

Keep `{0}`, `{1:0.#}` and date-format tokens exactly, including their occurrence counts and field alignment; ordering may change for natural grammar. Literal braces use `{{` and `}}`. Translations cannot introduce large alignment padding. Use `\n` for line breaks. No duplicate/unknown IDs, empty values, hidden direction override characters or unsafe controls. Files are limited to 1 MiB, depth eight, 2,048 messages and 4,096 characters per value. UTF-8 with or without a BOM is supported; UTF-16 is not. Schema/culture/direction and runtime composite-format checks run before installation, so a rejected replacement leaves the previous pack untouched.

## Grammar, direction and formatting

`plurals` supplies integer categories `zero`, `one`, `two`, `few`, `many`, `other`, with `other` required. Shipped account/reset-credit counts use language-specific singulars; missing messages also use English's plural forms. Rules cover shipped languages and tested Arabic/Russian integer cases; additional languages with different rules need a reviewed rule/test contribution or a neutral `other` expression. Fractional counts are not accepted by this API. [CLDR rules](https://www.unicode.org/cldr/charts/48/supplemental/language_plural_rules.html)

`culture` controls numeric separators and UI language; Gregorian calendar formatting is explicit. Existing displayed timezone/date semantics remain labeled **KST**, with UTC timestamps unchanged. Dates are not silently switched to the computer's timezone when the language changes.

`direction` must match the culture. RTL layouts are supported for local packs; technical names, percentages, durations, dates and resource values retain LTR ordering. An Arabic partial fixture is exercised in parser/culture tests and Windows UI evidence, but it is not advertised as a complete built-in Arabic translation. [Application evidence](evidence/language-packs/README.md)

Windows uses the selected UI culture with the system's `Global User Interface` composite font fallback, so Chinese/Japanese text does not inherit a Korean-only fallback. Missing-script fonts still depend on the operating system's installed language fonts. [WPF localization and font fallback](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-globalization-and-localization-overview)

## Validation and review

```text
python scripts/verify-localization.py
dotnet run --project tests/localization/CodexSwitch.Localization.Tests.csproj -c Release --no-launch-profile -p:UseSharedCompilation=false
```

CI checks complete built-in coverage, source mappings, formats/plurals/controls, and production parser/install/reload/restore APIs on Windows, Mac and Ubuntu. Windows rendering checks are separate from native Mac GUI/file-picker and real account acceptance. Security/technical errors from unknown upstream components may remain in their original language.

English/Korean preserve the previously authored catalogues. The seven new catalogues are AI-assisted initial translations with structural and Windows visual checks; native-speaker linguistic review remains welcome and is not implied by coverage. Traditional Chinese starts from the translated Simplified Chinese using [OpenCC](https://github.com/BYVoid/OpenCC) regional conversion, with explicit Taiwan terminology adjustments. OpenCC is not a runtime/build dependency of the shipped app.

Review concise captions, warnings, placeholders, bidi/numeric order, font fallback and minimum-window clipping in both themes. Report OS/CPU/culture and an irreversibly redacted or fictional program screenshot. Never post account names, private chats, tokens or unredacted originals. [Contributing](../CONTRIBUTING.md)
