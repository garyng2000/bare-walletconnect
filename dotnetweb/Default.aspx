<%@ Page Language="C#" AutoEventWireup="true" CodeFile="Default.aspx.cs" Inherits="_Default" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
</head>
<body>
    <form id="form1" runat="server">
        <div>
            <asp:TextBox ID="cWcUrl" runat="server"></asp:TextBox>
            <asp:Button ID="cConnectWallet" runat="server" OnClick="cConnectWallet_Click" Text="Connect Wallet" />
            <asp:Button ID="cConnectDApp" runat="server" OnClick="cConnectDApp_Click" Text="Conect Dapp" />

        </div>
    </form>
</body>
</html>
