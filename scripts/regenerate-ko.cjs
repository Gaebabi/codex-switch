const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const english = JSON.parse(fs.readFileSync(path.join(root, 'localization/Strings.en.json'), 'utf8'));
const korean = Object.fromEntries(Object.keys(english).map(key => [key, key]));
fs.writeFileSync(path.join(root, 'localization/Strings.ko.json'), JSON.stringify(korean, null, 2) + '\n', 'utf8');
