const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../deploy/vehicle-insurance/scum/assets/insurance.js'), 'utf8');
const start = source.indexOf(' function vehicleIconUrl(');
const end = source.indexOf('\n function appendVehicleIcon', start);
assert(start >= 0 && end > start, 'icon helper exists');
const context = {};
vm.runInNewContext(source.slice(start, end), context);
for (const model of ['Cruiser', 'BPC_Cruiser', 'BP_Cruiser_C', 'Cruiser_ES', 'Vehicle:BPC_Cruiser', 'BPC_Cruiser_ES_C']) {
  assert.equal(context.vehicleIconUrl(model), 'https://icons.gghost.games/icons/ICO_Cruiser.webp');
}
for (const invalid of ['', null, '../Cruiser', 'https://example.com/icon', '<img>']) {
  assert.equal(context.vehicleIconUrl(invalid), '');
}
assert(source.includes('appendVehicleIcon(header,v.name)'), 'owned vehicles use shared icon helper');
assert(source.includes('appendVehicleIcon(header,p.spawnClass||p.vehicleName)'), 'policies use shared icon helper');
console.log('13 icon checks passed. No network requests.');
