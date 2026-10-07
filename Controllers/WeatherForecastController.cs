using Application.Features.Users.Commands;
using Application.Features.Users.Queries;
using Application.Interfaces;
using Application.Interfaces.Security;
using Casbin;
using Domain.Entities;
using Infrastructure.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paramore.Brighter;
using Paramore.Darker;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class WeatherForecastController : ControllerBase
    {
        private readonly IApplicationDbContext _dbCtx;
        private readonly IAmACommandProcessor _commandProcessor;
        private readonly IQueryProcessor _queryProcessor;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly IEnforcer _enforcer;
        public WeatherForecastController(IApplicationDbContext dbCtx, IAmACommandProcessor commandProcessor, IQueryProcessor queryProcessor, IPasswordHasher passwordHasher, ITokenService tokenService, IEnforcer enforcer)
        {
            _dbCtx = dbCtx;
            _commandProcessor = commandProcessor;
            _queryProcessor = queryProcessor;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _enforcer = enforcer;
        }

        [HttpGet]
        public async Task<IActionResult> InitBaseUserAndTenant(CancellationToken cancellationToken)
        {
            

            if (await _dbCtx.Users.AnyAsync(u => u.Id == BaseConfig.SysUserId))
                return BadRequest(new {error = true, message = "己被始化"});

            // 增加基礎使用者
            var arcStudio = new Tenant("Arc Studio", "ArcStudio", BaseConfig.SysUserId)
            {
                Id = BaseConfig.ArcStudioTenantId,
            };
            _dbCtx.Tenants.Add(arcStudio);

            var arc = new User(arcStudio.Id, "阿昕", "Arc", "arc", null, Convert.ToDateTime("1978/05/09").ToUniversalTime(), _passwordHasher.HashPassword("1234"), BaseConfig.SysUserId); ;

            _dbCtx.Users.Add(arc);

            await _dbCtx.SaveChangesAsync(cancellationToken);

            // 增加底層使用者
            var sysUser = new User(arcStudio.Id, "系統管理員", "SysUser", "sysUser", null, Convert.ToDateTime("2026/09/30").ToUniversalTime(), _passwordHasher.HashPassword("1234"), BaseConfig.SysUserId)
            {
                Id = BaseConfig.SysUserId,
            };
            _dbCtx.Users.Add(sysUser);

            await _dbCtx.SaveChangesAsync(cancellationToken);

            return Ok();
        }

        // http://localhost:5106/WeatherForecast/CreateUser?userName=apple2&birthday=1982/07/30
        [HttpGet]
        public async Task<IActionResult> CreateUser([FromQuery] string userName, [FromQuery] string account, [FromQuery] string password, [FromQuery] DateTime? birthday, CancellationToken cancellationToken)
        {
            try
            {
                var command = new CreateUserCommand(userName, account, password,
                    BaseConfig.ArcStudioTenantId, birthday.HasValue ? birthday.Value.ToUniversalTime() : null,
                    BaseConfig.SysUserId);
                await _commandProcessor.SendAsync(command);

                return Ok(command);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = true, message = ex.Message });
            }
        }

        // http://localhost:5106/WeatherForecast/GetUser?userId=02bf66e3-59b2-4065-8f2e-c0e9f9505e12
        [HttpGet]
        public async Task<IActionResult> GetUser([FromQuery] Guid userId, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _queryProcessor.ExecuteAsync(new GetUserQuery(userId));

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = true, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTenant([FromQuery] string code, CancellationToken cancellationToken)
        {
            try
            {
                ArgumentNullException.ThrowIfNullOrWhiteSpace(code);
                
                var arcStudio = await _dbCtx.Tenants.Include(t => t.CreateUser)
                    .Where(t => t.Code == code).Select(t => new Tenant(t.Name, t.Code, t.CreateUserId, t.CreateAt) { Id = t.Id }).FirstOrDefaultAsync();

                return Ok(arcStudio);
            }
            catch (Exception ex)
            {
                return BadRequest(new {error = true, message = ex.Message});
            }
        }

        // http://localhost:5106/WeatherForecast/SearchUsers?keyword=
        [HttpGet]
        public async Task<IActionResult> SearchUsers([FromQuery] string? keyword, CancellationToken cancellationToken)
        {
            try
            {
                var searchUsersQuery = new SearchUsersQuery(BaseConfig.ArcStudioTenantId, keyword);
                var arcStudio = await _queryProcessor.ExecuteAsync(searchUsersQuery);
                return Ok(arcStudio);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = true, message = ex.Message });
            }
        }

        // http://localhost:5106/WeatherForecast/PasswordToHash?password=1234
        [HttpGet]
        public async Task<IActionResult> PasswordToHash([FromQuery] string password, CancellationToken cancellationToken)
        {
            try
            {
                ArgumentNullException.ThrowIfNullOrWhiteSpace(password);

                string passwordHash = _passwordHasher.HashPassword(password);

                return Ok(new { passwordHash,  });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = true, message = ex.Message });
            }
        }

        // http://localhost:5106/WeatherForecast/LoginUser?account=arc&password=1234
        [HttpGet]
        public async Task<IActionResult> LoginUser([FromQuery] string account, string password, CancellationToken cancellationToken)
        {
            try
            {
                ArgumentNullException.ThrowIfNullOrWhiteSpace(account);
                ArgumentNullException.ThrowIfNullOrWhiteSpace(password);

                var targetUser = await _queryProcessor.ExecuteAsync(new GetUserByAccountQuery(account));
                var isPass = _passwordHasher.VerifyPassword(password, targetUser.PasswordHash);

                if (!isPass) 
                    return Ok(new { loginStatus = false, message = "驗證錯誤"});

                string jwt = _tokenService.GenerateJwtToken(targetUser.Account, targetUser.Name, "admin");

                return Ok(new { loginStatus = true, jwt });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = true, message = ex.Message });
            }
        }

        // http://localhost:5106/WeatherForecast/TestAuth?role=arc&obj=data&act=read
        [HttpGet]
        public async Task<IActionResult> TestAuth([FromQuery] string role, [FromQuery] string obj, [FromQuery] string act, CancellationToken cancellationToken)
        {
            var result = await _enforcer.EnforceAsync(role, obj, act);
            return Ok(result);
        }
    }
}
