// 使用真实前端脚本验证监听归属，不连接浏览器或真实 .NET 电路。
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const listeners = new Set();
const context = {
    document: {
        addEventListener: (_, listener) => listeners.add(listener),
        removeEventListener: (_, listener) => listeners.delete(listener)
    }
};
context.window = context;
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../Snet.Iot.Daq.Web/wwwroot/js/app.js'), 'utf8'), context);
const outside = context.snet.outsideClick;
let firstCalls = 0;
let secondCalls = 0;
outside.register({ invokeMethodAsync: () => { firstCalls++; return Promise.resolve(); } }, 'old-page');
outside.register({ invokeMethodAsync: () => { secondCalls++; return Promise.resolve(); } }, 'new-page');
assert.equal(listeners.size, 1);
// 旧组件的延迟释放不能移除新组件拥有的监听。
outside.unregister('old-page');
assert.equal(listeners.size, 1);
const click = [...listeners][0];
click({ target: { closest: () => ({}) } });
assert.equal(secondCalls, 0);
click({ target: { closest: () => null } });
assert.equal(firstCalls, 0);
assert.equal(secondCalls, 1);
assert.equal(listeners.size, 0);
outside.unregister('new-page');
assert.equal(listeners.size, 0);
console.log('PASS: menu listener ownership, replacement, outside click and idempotent unregister');
