(this["webpackJsonpoas-webview"]=this["webpackJsonpoas-webview"]||[]).push([[0],{293:function(n,o,e){},294:function(n,o,e){},307:function(n,o){},315:function(n,o){},332:function(n,o){},334:function(n,o){},351:function(n,o){},352:function(n,o){},413:function(n,o){},415:function(n,o){},447:function(n,o){},449:function(n,o){},450:function(n,o){},455:function(n,o){},457:function(n,o){},463:function(n,o){},465:function(n,o){},478:function(n,o){},490:function(n,o){},493:function(n,o){},497:function(n,o){},509:function(n,o){},566:function(n,o){},638:function(n,o){},711:function(n,o){},725:function(n,o,e){"use strict";e.r(o);var t=e(98),c=e.n(t),s=e(284),i=e.n(s),a=(e(293),e(6)),r=e(4),u=e.p+"static/media/logo.6ce24c58.svg",l=(e(294),e(295),e(100)),f=(e(559),e(13)),g=e(12),d=e(99),h=e.n(d),p=e(154),j=e.n(p),b=e(155),m=function(n){Object(f.a)(e,n);var o=Object(g.a)(e);function e(n,t){return Object(r.a)(this,e),o.call(this,{cryptoLib:b,sessionStorage:n.sessionStorage,connectorOpts:n,pushServerOpts:t})}return e}(j.a),v=e(31),w=l.safeJsonParse,O=l.safeJsonStringify,S=function n(){var o=this;Object(r.a)(this,n),this.setSession=function(n){return console.log("set session"),console.log(n),o.session=n,sessionStorage.setItem("walletconnect",O(n)),n},this.removeSession=function(){sessionStorage.removeItem("walletconnect"),console.log("remove session")},this.getSession=function(){console.log("get session");try{return w(sessionStorage.getItem("walletconnect"))}catch(n){return o.session}}},y=function(){function n(){Object(r.a)(this,n)}return Object(a.a)(n,[{key:"connect",value:function(n){var o=h.a,e=new S,t=new m({bridge:"https://wcbridge.garyng.com",qrcodeModal:o,sessionStorage:e,session:undefined});this.connector=t,console.log(t),t.on("connect",(function(n,o){if(console.log("connect "),n)throw console.log("connect "),n;console.log(o);var e=o.params[0];e.accounts,e.chainId})),t.on("session_update",(function(n,o){if(console.log("session_udate"),n)throw n;var e=o.params[0];e.accounts,e.chainId})),t.on("disconnect",(function(n,o){if(console.log("disconnect "),n)throw n})),t.on("modal_closed",(function(n,o){if(console.log("modal_closed "),n)throw n})),t.connect().then((function(n){console.log(n);var o={id:+Date.now(),jsonrpc:"2.0",method:"eth_accounts",parameters:[]};t.sendCustomRequest(o).then((function(n){console.log(n)})).catch((function(n){console.log(n)}))})).catch((function(n){console.log(n)})).finally((function(){o.close()}))}},{key:"sendTransaction",value:function(n,o,e){}},{key:"callFunction",value:function(n,o,e){}}]),n}();var x=function(){return window.wc_apiService=new y,Object(v.jsx)("div",{className:"App",children:Object(v.jsxs)("header",{className:"App-header",children:[Object(v.jsx)("img",{src:u,className:"App-logo",alt:"logo"}),Object(v.jsxs)("p",{children:["Edit ",Object(v.jsx)("code",{children:"src/App.js"})," and save to reload."]}),Object(v.jsx)("a",{className:"App-link",href:"https://reactjs.org",target:"_blank",rel:"noopener noreferrer",children:"Learn React"})]})})},k=function(n){n&&n instanceof Function&&e.e(3).then(e.bind(null,731)).then((function(o){var e=o.getCLS,t=o.getFID,c=o.getFCP,s=o.getLCP,i=o.getTTFB;e(n),t(n),c(n),s(n),i(n)}))};i.a.render(Object(v.jsx)(c.a.StrictMode,{children:Object(v.jsx)(x,{})}),document.getElementById("root")),k()}},[[725,1,2]]]);
//# sourceMappingURL=main.c3c62002.chunk.js.map