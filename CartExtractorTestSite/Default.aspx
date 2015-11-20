<%@ Page Language="C#" AutoEventWireup="true" CodeFile="Default.aspx.cs" Inherits="_Default" ValidateRequest="false"
    MaintainScrollPositionOnPostback="true" %>

<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>4-Tell Extractor Test Site</title>
    <style type="text/css">
        .style1 {
            width: 700px;
            height: 111px;
        }

        .style3 {
            width: 140px;
        }

        .style4 {
            width: 273px;
            height: 30px;
        }

        .style5 {
            width: 500px;
            height: 30px;
        }

        .style7 {
            width: 135px;
            height: 30px;
            align-content: center;
        }

        .style9 {
            width: 285px;
            height: 30px;
        }

        .style10 {
            width: 250px;
        }

        .style15 {
            width: 416px;
            height: 30px;
        }

        .style16 {
            width: 320px;
            height: 30px;
        }

        .auto-style1 {
            width: 514px;
            height: 30px;
        }

        .auto-style2 {
            width: 178px;
            height: 30px;
            align-content: center;
        }
    </style>
</head>
<body>
    <form id="form1" runat="server" defaultfocus="TextBoxClientAlias">
        <table cellpadding="0" cellspacing="0" class="style1">
            <tr>
                <asp:ScriptManager ID="ScriptManager1" runat="server"></asp:ScriptManager>
                <td align="center">
                    <asp:Label ID="Label1" runat="server" Font-Bold="True" Font-Size="X-Large"
                        Text="4-Tell Extractor Test Site" Font-Names="Arial Black"></asp:Label>
                </td>
            </tr>
        </table>
        <div>
        </div>
        <table class="style1">
            <tr>
                <td class="style16"></td>
                <td class="style16">
                    <asp:Label ID="Label3" runat="server" Font-Bold="False" Font-Size="Large" Text="Extraction Type" Font-Names="Arial"></asp:Label>
                </td>
                <td class="style10">
                    <asp:DropDownList ID="DropDownListExtractType" runat="server" TabIndex="1" AutoPostBack="True" Width="155px" OnSelectedIndexChanged="DropDownListExtractType_SelectedIndexChanged">
                        <asp:ListItem>Full</asp:ListItem>
                        <asp:ListItem>Update</asp:ListItem>
                        <asp:ListItem>Catalog</asp:ListItem>
                        <asp:ListItem>Inventory</asp:ListItem>
                        <asp:ListItem>Sales</asp:ListItem>
                        <asp:ListItem>Customers</asp:ListItem>
                    </asp:DropDownList>
                </td>
                <td class="style10">&nbsp;
                    <asp:Button ID="BeginExtraction" runat="server" Text="Begin" TabIndex="3" Width="100px" OnClick="BeginExtraction_Click" />
                </td>
            </tr>
            <tr>
                <td align="right">&nbsp;</td>
            </tr>
            <tr>
                <td class="style16"></td>
                <td class="style16">
                    <asp:Label ID="Label8" runat="server" Font-Bold="False" Font-Size="Large" Text="Client Alias" Font-Names="Arial"></asp:Label>
                </td>
                <td class="style10">
                    <asp:TextBox ID="TextBoxClientAlias" runat="server" Width="150px" TabIndex="2" Columns="1" OnTextChanged="TextBoxClientAlias_TextChanged"></asp:TextBox>
                </td>
                <td class="style10">&nbsp;
                    <asp:Button ID="CancelExtraction" runat="server" Text="Cancel" TabIndex="4" Width="100" OnClick="CancelExtraction_Click" />
                </td>
            </tr>

            <tr>
                <td align="right">&nbsp;
                    <br />
                    <br />
                </td>
            </tr>
        </table>
        <asp:UpdatePanel ID="UpdatePanelProgress" runat="server" UpdateMode="Conditional">
            <ContentTemplate>
                <asp:Timer ID="ProgressTimer" runat="server" OnTick="ProgressTimer_Tick"></asp:Timer>
                <table cellpadding="0" cellspacing="0" class="style1">
                    <tr>
                        <td class="style3" valign="top">
                            <asp:Label ID="Label45" runat="server" Font-Bold="False" Font-Size="Large" Text="Extraction Progress" Font-Names="Arial"></asp:Label>
                            &nbsp;
						    <br />
                            <br />
                            <br />
                            <asp:CheckBox ID="CheckBoxExtractPause" runat="server" Text="Pause" OnCheckedChanged="CheckBoxExtractPause_CheckedChanged" Checked="True" AutoPostBack="True" Visible="false"/>
                        </td>
                        <td>
                            <asp:TextBox ID="TextBoxExtractProgress" runat="server" Font-Bold="true" Font-Size="Medium"
                                Font-Names="Arial" Width="500px" BorderColor="Black"
                                Height="400px" TextMode="MultiLine"
                                Style="margin-left: 0px; margin-right: 0px;" BorderStyle="Solid" ReadOnly="True"></asp:TextBox>
                        </td>
                    </tr>
                </table>
            </ContentTemplate>
        </asp:UpdatePanel>
        <table cellpadding="0" cellspacing="0" class="style5">
            <tr>
                <td>&nbsp;</td>
            </tr>
        </table>
        <asp:UpdatePanel ID="UpdatePanelResults" runat="server" UpdateMode="Conditional">
            <ContentTemplate>
                <table cellpadding="0" cellspacing="0" class="style1">
                    <tr>
                        <td class="style3" valign="top">
                            <asp:Label ID="Label2" runat="server" Font-Bold="False" Font-Size="Large" Width="100px" Text="Extraction Results" Font-Names="Arial"></asp:Label>
                        </td>
                        <td>
                            <asp:TextBox ID="TextBoxResults" runat="server" Font-Bold="true" Font-Size="Medium"
                                Font-Names="Arial" Width="500px" BorderColor="Black"
                                Height="200px" TextMode="MultiLine"
                                Style="margin-left: 0px; margin-right: 0px;" BorderStyle="Solid" ReadOnly="True"></asp:TextBox>
                        </td>
                    </tr>
                </table>
            </ContentTemplate>
        </asp:UpdatePanel>
    </form>
    <script type="text/javascript">
        var xPos, yPos;
        var prm = Sys.WebForms.PageRequestManager.getInstance();

        prm.add_beginRequest(BeginRequestHandler);
        prm.add_endRequest(EndRequestHandler);

        function BeginRequestHandler(sender, args) {
            xPos = $get('TextBoxExtractProgress').outerHeight;
            yPos = $get('TextBoxExtractProgress').scrollTop;
            zPos = $get('TextBoxExtractProgress').scrollHeight;
        }

        function EndRequestHandler(sender, args) {
            if (zPos < yPos + 400) {
                $get('TextBoxExtractProgress').scrollTop = zPos;
            }
            else
                $get('TextBoxExtractProgress').scrollTop = yPos;
        }
    </script>
</body>
</html>
