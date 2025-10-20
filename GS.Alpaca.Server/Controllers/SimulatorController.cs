using ASCOM.Common.Alpaca;
using GS.Simulator;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using ASCOM.Alpaca;

namespace GS.Alpaca.Server
{
    /// <summary>
    /// OmniSim only, not part of Alpaca
    /// </summary>
    [ServiceFilter(typeof(AuthorizationFilter))]
    [ApiExplorerSettings(GroupName = "OmniSim")]
    [ApiController]
    [Route("simulator/v1/")]
    public class SimulatorController : ProcessBaseController
    {
        public const string APIRoot = "simulator/v1/";

        private Simulator.Telescope TelescopeAccess(uint InstanceID)
        {
            return DeviceManager.GetTelescope((uint)InstanceID) as Simulator.Telescope;
        }

        /// <summary>
        /// OmniSim only API - Resets a device settings to the simulator default
        /// </summary>
        /// <param name="DeviceNumber">Zero based device number as set on the server (A uint32 with a range of 0 to 4294967295)</param>
        /// <param name="ClientID">Client's unique ID.</param>
        /// <param name="ClientTransactionID">Client's transaction ID.</param>
        /// <response code="200">Transaction complete or exception</response>
        /// <response code="400" examples="Error message describing why the command cannot be processed">Method or parameter value error, check error message</response>
        /// <response code="500" examples="Error message describing why the command cannot be processed">Server internal error, check error message</response>
        [HttpPut]
        [Produces(MediaTypeNames.Application.Json)]
        [Route("telescope/{DeviceNumber}/reset")]
        public ActionResult<Response> ResetTelescope(
            [Required][DefaultValue(0)][SwaggerSchema(Strings.DeviceIDDescription, Format = "uint32")][Range(0, 4294967295)] uint DeviceNumber,
            [SwaggerSchema(Strings.ClientIDDescription, Format = "uint32")][Range(0, 4294967295)] uint ClientID = 0,
            [SwaggerSchema(Strings.ClientTransactionIDDescription, Format = "uint32")][Range(0, 4294967295)] uint ClientTransactionID = 0)
        {
            return ProcessRequest(() =>
            {
                Simulator.TelescopeHardware.ClearProfile();
                Simulator.TelescopeHardware.Init();
            },
            DeviceManager.ServerTransactionID, ClientID, ClientTransactionID, $"Reseting Telescope {DeviceNumber} to default settings.");
        }

        #region Restart devices

        /// <summary>
        /// OmniSim only API - Restarts a device simulator to the simulator stored settings and a clean state. This can be used to restart a device so it behaves like the OmniSim server was just freshly started, without restarting the whole OmniSim.
        /// </summary>
        /// <param name="DeviceNumber">Zero based device number as set on the server (A uint32 with a range of 0 to 4294967295)</param>
        /// <param name="ClientID">Client's unique ID.</param>
        /// <param name="ClientTransactionID">Client's transaction ID.</param>
        /// <response code="200">Transaction complete or exception</response>
        /// <response code="400" examples="Error message describing why the command cannot be processed">Method or parameter value error, check error message</response>
        /// <response code="500" examples="Error message describing why the command cannot be processed">Server internal error, check error message</response>
        [HttpPut]
        [Produces(MediaTypeNames.Application.Json)]
        [Route("telescope/{DeviceNumber}/restart")]
        public ActionResult<Response> RestartTelescope(
            [Required][DefaultValue(0)][SwaggerSchema(Strings.DeviceIDDescription, Format = "uint32")][Range(0, 4294967295)] uint DeviceNumber,
            [SwaggerSchema(Strings.ClientIDDescription, Format = "uint32")][Range(0, 4294967295)] uint ClientID = 0,
            [SwaggerSchema(Strings.ClientTransactionIDDescription, Format = "uint32")][Range(0, 4294967295)] uint ClientTransactionID = 0)
        {
            return ProcessRequest(() =>
            {
                //Only supports 1 right now, in the future use DeviceNumber instead.
                DriverManager.LoadTelescope(0);
            },
            DeviceManager.ServerTransactionID, ClientID, ClientTransactionID, $"Restarting Telescope {DeviceNumber} to a clean state.");
        }

        #endregion Restart devices

        #region XMLProfile

        /// <summary>
        /// Gets a copy of the profile. This is returned as a string value in the standard Response Value field.
        /// </summary>
        /// <remarks>
        /// <para>This method returns the version of the ASCOM device interface contract to which this device complies. Only one interface version is current at a moment in time and all new devices should be built to the latest interface version. Applications can choose which device interface versions they support and it is in their interest to support previous versions as well as the current version to ensure they can use the largest number of devices.</para>
        /// </remarks>
        /// <param name="DeviceNumber">Zero based device number as set on the server (A uint32 with a range of 0 to 4294967295)</param>
        /// <param name="ClientID">Client's unique ID.</param>
        /// <param name="ClientTransactionID">Client's transaction ID.</param>
        /// <response code="200">Transaction complete or exception</response>
        /// <response code="400" examples="Error message describing why the command cannot be processed">Method or parameter value error, check error message</response>
        /// <response code="500" examples="Error message describing why the command cannot be processed">Server internal error, check error message</response>
        [HttpGet]
        [Produces(MediaTypeNames.Application.Json)]
        [Route("telescope/{DeviceNumber}/xmlprofile")]
        public ActionResult<StringResponse> GetXMLTelescope(
            [Required][DefaultValue(0)][SwaggerSchema(Strings.DeviceIDDescription, Format = "uint32")][Range(0, 4294967295)] uint DeviceNumber,
            [SwaggerSchema(Strings.ClientIDDescription, Format = "uint32")][Range(0, 4294967295)] uint ClientID = 0,
            [SwaggerSchema(Strings.ClientTransactionIDDescription, Format = "uint32")][Range(0, 4294967295)] uint ClientTransactionID = 0)
        {
            return ProcessRequest(() => (TelescopeAccess(DeviceNumber) as ISimulation).GetXMLProfile(), DeviceManager.ServerTransactionID, ClientID, ClientTransactionID);
        }

        #endregion XMLProfile
    }
}