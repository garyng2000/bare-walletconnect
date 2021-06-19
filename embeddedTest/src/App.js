import logo from './logo.svg';
import './App.css';
import IFrame from './IFrame';
import { useState, useRef, useEffect, useCallback } from 'react';
import QRCodeModal from "@walletconnect/qrcode-modal";
import { Button } from 'react-dom';
import { jsxOpeningElement } from '@babel/types';


function App() {
  const iframeRef = useRef();
  var myMeta = {
    'description': 'Testing React DApp',
    'name': 'test react via iframe',
    'url': 'https://www.blabla.com/',
    'icons': ['https://blabla.com/favicon.png']
  };
  const [jsonrpc, setJsonrpc] = useState(
    JSON.stringify({
      id: 1,
      jsonrpc: "2.0",
      method: 'wc_create_session',
      params: [
        //        "https://bridge.walletconnect.org",
        "https://wcbridge.garyng.com",
        myMeta,
      ]
    })
  );
  const [jsonrpcResult, setJsonrpcResult] = useState('');

  const [wcSessionUri, setWCSessionUri] = useState();
  //const webview_api = process.env.PUBLIC_URL + '/oas-webview/index.html'
  const webview_api = 'http://localhost:4000/index.html'
  window.outstandingRpc = {};

  const handleJsonRpc = (jsonrpc) => {
    var { id, method, params, result, error } = jsonrpc;
    console.log(jsonrpc, iframeRef);
    const response = {
      id: id,
      jsonrpc: "2.0",
      result: jsonrpc,
    }
    iframeRef.current.jsonRpcResult(response);
    setJsonrpcResult(JSON.stringify(jsonrpc));
  }

  const callJsonRpc = (evt) => {
    console.log(jsonrpc, iframeRef);
    const id = Date.now();
    const rpcCall = {
      ...JSON.parse(jsonrpc),
      id: id
    };
    window.outstandingRpc[rpcCall.id] = rpcCall;
    const { method } = rpcCall;
    iframeRef.current.jsonrpcCall(rpcCall);
    const task = new Promise(function (resolve, reject) {
      const eventName = '' + id;
      const handler = (evt) => {
        const { id, pendingCall, callResult } = evt.detail;
        console.log(evt);
        if (callResult.result) resolve(callResult.result);
        else reject(callResult.error);
        window.removeEventListener(eventName, handler, false);
      };
      window.addEventListener(eventName, handler, false);
    });

    return task.then((result) => {
      console.log(method, result);
      if (method === "wc_connect") {
        const response = {
          id: result.id,
          jsonrpc: '2.0',
          result: {
            approved: true,
          },
        }
        iframeRef.current.jsonRpcResult(response);
      }
      setJsonrpcResult(JSON.stringify(result));

      return result;
    }).catch(err => {
      console.log(err);
      return Promise.reject(err);
    })
  }

  useEffect(() => {
    // freakingly import!!! this useXXX is way worse than the good old class based !
    if (typeof window.listenerAdded == 'undefined') {
      window.addEventListener('message', (evt) => {
        console.log(evt);
        if (evt.data) {
          var dataObject = evt.data || {};
          var sourceWindow = evt.source;
          var origin = evt.origin;
          console.log(dataObject);
          var { target, jsonrpc } = dataObject;
          if (jsonrpc) {
            console.log(evt);
            var { id, method, params, result, error } = jsonrpc;
            console.log(id, method, params, result, error);
            if (method === 'display_uri') {
              console.log('qr code open');
              QRCodeModal.open(params[0], () => {
                console.log('qr code closed by user');
              }, params[1]);
            }
  
            else if (method === 'undisplay_uri') {
              QRCodeModal.close();
            }
            else if (method === 'session_request') {
              console.log(method, jsonrpc);
            }
            else if (!method && id) {
              // call result
              console.log(window.outstandingRpc);
              const pendingCall = window.outstandingRpc[id];
              console.log(pendingCall);
              if (pendingCall || true) {
                delete window.outstandingRpc[id];
                const eventName = '' + id;
                const wc_event = {
                  pendingCall: pendingCall,
                  callResult: jsonrpc,
                  id: id,
                }
                const event = new CustomEvent(eventName, {
                  detail: wc_event
                });
                window.dispatchEvent(event);
                setJsonrpcResult(JSON.stringify(wc_event));
              }
            }
            else if (method) {
              setTimeout(() => {
                handleJsonRpc(jsonrpc);              
              }, 0);
            }
          }
        }
      })
      window.listenerAdded = true;
    }
  
  },[setJsonrpcResult, iframeRef])

  return (
    <div className="App">
      <header className="App-header">
        <p>
          Edit <code>src/App.js</code> and save to reload.
        </p>
        <IFrame ref={iframeRef} src={webview_api}>

        </IFrame>
        <textarea style={{ width: "600px", height: "300px" }} value={jsonrpc} onChange={e => setJsonrpc(e.target.value)} />
        <button onClick={callJsonRpc}> run this </button>
        <textarea style={{ width: "600px", height: "300px" }} readOnly value={jsonrpcResult} />
      </header>
    </div>
  );
}

export default App;