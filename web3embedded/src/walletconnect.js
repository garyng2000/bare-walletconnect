
import QRCodeModal from "@walletconnect/qrcode-modal";
import Connector from "@walletconnect/core";
import * as cryptoLib from "@walletconnect/iso-crypto";


class MyWalletConnect extends Connector {
    constructor(connectorOpts, pushServerOpts) {
        super({
            cryptoLib,
            sessionStorage: connectorOpts.sessionStorage,
            transport: connectorOpts.transport,
            connectorOpts,
            pushServerOpts,
        });
    }
}

export default MyWalletConnect;
