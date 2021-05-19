using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Threading.Tasks;
using GN.WalletConnect;

public partial class _Default : System.Web.UI.Page
{
    protected void Page_Load(object sender, EventArgs e)
    {
        
        WalletConnect wc = new WalletConnect();
    }

    protected void cConnectWallet_Click(object sender, EventArgs e)
    {
        var t = Task.Run(async () =>
        {
            try
            {
                var myMeta = new { name = ".NET Dapp", accounts = new List<string>() };
                var wcSessionRequest = await WCSession.CreateSession("https://wcbridge.garyng.com", myMeta);
                var wcUrl = wcSessionRequest.wcUri;

                //var result = await wcSessionRequest.wcSessionRequest.Value;
                var x = 1;
                return wcUrl;
            } catch (Exception ex)
            {
                var r = ex.Message;
                return null;
            }

        });
        t.ConfigureAwait(false);
        t.Wait();
        var xx = t.Result;
        cWcUrl.Text = xx;

    }

    protected void cConnectDApp_Click(object sender, EventArgs e)
    {
        var t = Task.Run(async () =>
        {
            try
            {
                var myMeta = new { name = ".NET Dapp", description = "some description", accounts = new List<string>() };
                var wcSessionRequest = await WCSession.ConnectSession(cWcUrl.Text, myMeta);
                var wcSession = wcSessionRequest.Key;
                var request = wcSessionRequest.Value;
                await wcSession.SendSessionRequestResponse(request, myMeta, new List<string>(), true, "https://kovan.infura.io", 42, true);
                return wcSessionRequest;
            }
            catch (Exception ex)
            {
                var r = ex.Message;
                throw;
            }

        });
        t.ConfigureAwait(false);
        t.Wait();
        var xx = t.Result;

    }
}