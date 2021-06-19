import React, { Component, createRef } from 'react'
import { createPortal } from 'react-dom'

class IFrame extends Component {
    constructor(props) {
        super(props);
        this.iframeRef = createRef();
        this.state = {
            head: null,
            body: null,
            contentWindow: null,
            title: null,
        }
        this.setContentRef = (contentRef) => {
            //console.log(contentRef);
            const contentWindow = (contentRef || {}).contentWindow;
            const contentDocument = (contentWindow || {}).document;
            const contentBody = (contentDocument || {}).body;
            const contentTitle = (contentDocument || {}).title;
            //console.log(contentWindow, contentDocument, contentBody, contentTitle);
            this.setState({
                contentWindow: (contentRef || {}).contentWindow,
                body: (((contentRef || {}).contentWindow || {}).document || {}).body,
                head: (((contentRef || {}).contentWindow || {}).document || {}).head,
                title: (((contentRef || {}).contentWindow || {}).document || {}).title
            });
            if (contentWindow) {
                setTimeout(() => {
                    //console.log('posting calling from parent');
                    contentWindow.postMessage('calling abc', '*');

                }, 0);
                contentWindow.addEventListener('load', (evt) => {
                    console.log('abc', evt);
                });
            }
        }
    }
    iframeLoad = (event) => {
        console.log('iframe loaded', event, event.target);
        const _this = this;
        //console.log(this.state);
        //console.log(_this.state.contentWindow);
        // setTimeout(() => {
        //     _this.jsonrpcCall({
        //         id: Date.now(),
        //         jsonrpc: "2.0",
        //         method: "wc_connect",
        //         params: []
        //     })
        // }, 500); // should delay further if it is large
    }

    jsonrpcCall = (calldata) => {
        if (this.state.contentWindow) {
            this.state.contentWindow.postMessage({
                target: 'iframe_webview',
                jsonrpc: calldata
            }, "*")
        }
        else {
            console.log('iframe not loaded yet');
        }
    }

    jsonRpcResult = (calldata) => {
        if (this.state.contentWindow) {
            this.state.contentWindow.postMessage({
                target: 'iframe_webview',
                jsonrpc: calldata
            }, "*")
        }
        else {
            console.log('iframe not loaded yet');
        }
    }
        
    componentDidMount() {
        //console.log(this.iframeRef);
        //console.log(this.iframeRef.document);
        //console.log(this.iframeRef.contentWindow);
        //this.iframeRef.contentWindow.addEventListener('load', this.iframeLoad);
        //this.iframeRef.addEventListener('load', this.iframeLoad);
    }

    render() {
        const { children, ...props } = this.props
        const { body } = this.state
        return (
            <iframe
                title='assigned title'
                {...props}
                //                ref={this.iframeRef}
                ref={this.setContentRef}
                onLoad={this.iframeLoad}

            >
                {/* {body && createPortal(children, body)} */}
            </iframe>
        )
    }
}

export default IFrame;