import logo from './logo.svg';
import './App.css';
import { useState, useRef, useEffect, useCallback } from 'react';
import Web3 from "web3";
import * as safeJsonUtils from "safe-json-utils";
import WalletConnectProvider from "@walletconnect/web3-provider";
import MyWalletConnect from "./walletconnect";
import QRCodeModal from "@walletconnect/qrcode-modal";
import SocketTransport from "@walletconnect/socket-transport";
import { getWalletRegistryUrl, getMobileLinkRegistry, formatMobileRegistry, formatMobileRegistryEntry, formatIOSMobile } from "@walletconnect/browser-utils";
import { parseWalletConnectUri, parsePersonalSign, parseTransactionData, uuid } from "@walletconnect/utils";

//import * as sigUtil from 'eth-sig-util';

const safeJsonParse = safeJsonUtils.safeJsonParse;
const safeJsonStringify = safeJsonUtils.safeJsonStringify;

class MySessionStorage {

  setSession = (session) => {
    console.log('set session');
    console.log(session);
    this.session = session;
    //sessionStorage.setItem('walletconnect', safeJsonStringify(session));
    return session;
  }

  removeSession = () => {
    //sessionStorage.removeItem('walletconnect');
    console.log('remove session');
  }
  getSession = () => {

    console.log('get session');
    try {
      //return safeJsonParse(sessionStorage.getItem('walletconnect'));
      return this.session;
    }
    catch (e) {
      return this.session;
    }
  }
}

class MyQRCodeModal {

  open = (uri, cb, qrcodeModalOptions) => {
    console.log('open QR code ', uri, qrcodeModalOptions);
    let mobileLinks = null;
    const { handshakeTopic, bridge, key } = parseWalletConnectUri(uri);
    const walletRegistryUrl = getWalletRegistryUrl();
    const id = (qrcodeModalOptions || {}).id;

    // ignore double firing in short order
    if (this.uri) return;

    this.uri = uri;
    this.id = id;
    this.handshakeTopic = handshakeTopic;

    const whitelist = qrcodeModalOptions.mobileLinks && qrcodeModalOptions.mobileLinks.map(s => s.toLowerCase());
    const registry = fetch(walletRegistryUrl)
      .then(x => x.json())
      .then(x => {
        // const mobile = formatMobileRegistry(x)
        //                 .filter(w=>{
        //                   return !whitelist || ((whitelist.indexOf(w.name.toLowerCase()) >= 0));
        //                 })
        //                 .map(w=>{
        //                   return {
        //                     ...w,
        //                     mobileLink: formatIOSMobile(uri, w),
        //                   }
        //                 });
        const mobile = Object.keys(x)
          .filter(k => {
            const entry = x[k];
            const { name } = entry.name;
            //console.log(x[k]);
            return (!!entry.mobile.universal || !!entry.mobile.native)
              &&
              (!whitelist || ((whitelist.indexOf(name.toLowerCase()) >= 0)));
          }
          ).map(k => {
            const w = x[k];
            const { native, universal } = w.mobile || {};
            const { ios, android } = w.app || {};
            return {
              ...w,
              ...formatMobileRegistryEntry(w),
            };
          }).map(w => {
            return {
              ...w,
              mobileLink: formatIOSMobile(uri, w)
            }
          });
        mobileLinks = mobile;
        return mobile;
      })
      .catch(err => {
        console.log(err);
      })
      .finally(() => {
        console.log('scan through mobile', mobileLinks);
      });
    const wc_event = {
      payload: uri,
      eventName: 'display_uri',
      id: id,
      handshakeTopic: handshakeTopic,
    }
    setTimeout(() => {
      const event = new CustomEvent('wc_event', {
        detail: wc_event
      });
      if (!id || id === this.id) {
        window.dispatchEvent(event);
      }
      // window.postMessage({
      //   name:'local',
      //   event: wc_event
      // },"*")        
    }, 0);
  }

  close = () => {
    console.log('close QR code ');
    const wc_event = {
      payload: {
        uri: this.uri || {},
        id: this.id,
        handshakeTopic: this.handshakeTopic
      },
      eventName: 'undisplay_uri'
    }
    setTimeout(() => {
      const event = new CustomEvent('wc_event', {
        detail: wc_event
      });
      window.dispatchEvent(event);
      // window.postMessage({
      //   name:'local',
      //   event: wc_event
      // },"*")        
    }, 0);
  }
}

class WCApiService {
  connect(opts) {
    const myQRModal = new MyQRCodeModal() || QRCodeModal;
    const { clientMeta, bridge, session, id, uri } = opts || {};
    const mySessionStorage = new MySessionStorage();
    const qrcodeModalOptions = {
      id: id || Date.now()
    }
    const { handshakeTopic, key } = (uri && parseWalletConnectUri(uri)) || {};
    const clientId = (session || {}).clientId || uuid();

    const getBridge = () => {
      return bridge ||
        (session || {}).bridge ||
        ((uri && parseWalletConnectUri(uri)) || {}).bridge
    };

    const transport = new SocketTransport({
      protocol: "wc",
      version: 1,
      url: getBridge(),
      subscriptions: [clientId],
    });

    const connector = new MyWalletConnect({
      uri: uri, // wallet mode(i.e. received wc://)
      clientMeta: clientMeta,
      bridge: bridge, // Required if no uri
      qrcodeModal: myQRModal,
      qrcodeModalOptions: qrcodeModalOptions,
      sessionStorage: mySessionStorage,
      session: session, // reconnect to existing session
      transport: transport, // if supplised must use the same client id
    });

    // only if transport has been passed in
    connector.clientId = clientId;

    this.connector = connector;

    console.log(connector);

    connector.on("connect", (error, payload) => {
      console.log('connect ', connector._transport, transport);
      if (error) {
        console.log('connect ');
        throw error;
      }

      console.log(payload, connector);
      // Get provided accounts and chainId
      const { accounts, chainId } = payload.params[0];
      const wc_event = {
        payload: {
          id: myQRModal.id,
          handshakeTopic: myQRModal.handshakeTopic,
          session: connector.session,
        },
        eventName: 'connect'
      }
      const event = new CustomEvent('wc_event', {
        detail: wc_event
      });
      window.dispatchEvent(event);

    });
    connector.on("session_update", (error, payload) => {
      console.log('session_udate');
      if (error) {
        throw error;
      }
      // Get updated accounts and chainId
      const { accounts, chainId } = payload.params[0];
    });

    connector.on("session_request", (error, payload) => {
      const mySession = connector.session;
      console.log('session_request', payload, mySession, handshakeTopic, id);
      const wc_event = {
        payload: {
          id: id,
          jsonrpc: '2.0',
          result: payload,
        },
        eventName: 'session_request'
      }
      const event = new CustomEvent('wc_event', {
        detail: wc_event
      });
      window.dispatchEvent(event);
    });

    connector.on("disconnect", (error, payload) => {
      console.log('disconnect ');
      if (error) {
        throw error;
      }
      // Delete connector
    });
    connector.on("modal_closed", (error, payload) => {
      console.log('modal_closed ');
      if (error) {
        throw error;
      }
      // Delete connector
    });

    connector.on("call_request", (error, payload) => {
      // capture all incoming request
      console.log('call_request', payload);
      const wc_event = {
        payload: payload,
        eventName: 'call_request'
      }
      const event = new CustomEvent('wc_event', {
        detail: wc_event
      });
      window.dispatchEvent(event);
    });

    connector.on("transport_open", () => {
      console.log('socket opened');
      const wc_event = {
        eventName: 'transport_open',
        payload: {}
      }
      const event = new CustomEvent('wc_event', {
        detail: wc_event
      });
      window.dispatchEvent(event);
    });

    connector.on("transport_close", () => {
      console.log('socket closed');
      const wc_event = {
        eventName: 'transport_close',
        payload: {}
      }
      const event = new CustomEvent('wc_event', {
        detail: wc_event
      });
      window.dispatchEvent(event);
    });

    connector.on("transport_error", () => {
      console.log(Date.now(), this, 'socket error', transport);
      // re-sub, hope the _subscriptions would not be optimized away by webpack etc.
      transport._subscriptions = [clientId];
      const wc_event = {
        eventName: 'transport_error',
        payload: {}
      }
      const event = new CustomEvent('wc_event', {
        detail: wc_event
      });
      window.dispatchEvent(event);
    });

    !uri && connector.connect()
      .then(result => {
        console.log(result);
        const rpcCall = {
          id: +Date.now(),
          jsonrpc: "2.0",
          method: "eth_accounts",
          parameters: []
        };
        // connector.sendCustomRequest(rpcCall)
        //   .then(result => {
        //     console.log(result);
        //   })
        //   .catch(err => {
        //     console.log(err);
        //   })
      })
      .catch(err => {
        console.log(err);
      })
      .finally(() => {
        myQRModal.close();
      })
  }

  approveSession(sessionStatus) {
    this.connector.approveSession(sessionStatus);
  }

  rejectSession(sessionError) {
    this.connector.rejectSession(sessionError);
  }

  approveRequest(jsonRpcResult) {
    this.connector.approveRequest(jsonRpcResult);
  }

  rejectRequest(jsonRpcError) {
    this.connector.rejectRequest(jsonRpcError);
  }

  sendRequest(jsonRpc) {
    return this.connector.unsafeSend(jsonRpc);
  }

  sendTransaction(contractAddress, functionName, params) {
  }

  callFunction(contractAddress, functionName, params) {

  }
}

function App() {
  const [bridge, setBridge] = useState('https://bridge.walletconnect.org');
  const [uri, setUri] = useState();
  const [session, setSession] = useState();

  const container = window.parent || window.opener;
  console.log("no parent/container", container === window);
  const clientMeta = {
    'description': 'Testing React DApp/Wallet',
    'name': 'test react via iframe',
    'url': 'https://www.blabla.com/',
    'icons': ['https://blabla.com/favicon.png']
  };
  window.wc_apiService = new WCApiService();
  if (!container || container === window) {
    // not embedded(for testing)
    // console.log('connect');
    // window.wc_apiService.connect({
    //   uri,
    //   bridge,
    //   clientMeta,
    //   session,
    // });
  }
  const connect = (evt) => {
    console.log('connect');
    window.wc_apiService.connect({
      uri,
      bridge,
      clientMeta,
      session,
    });

  }
  return (
    <div className="App">
      <header className="App-header">
        <img src={logo} className="App-logo" alt="logo" />
        <p>
          Edit <code>src/App.js</code> and save to reload.
        </p>
        <input onChange={e => setBridge(e.target.value)} value={bridge} />
        <input onChange={e => setUri(e.target.value)} value={uri} />
        <input onChange={e => setSession(e.target.value)} value={session} />
        <button onClick={connect}> run this </button>
      </header>
    </div>
  );
}

export default App;
