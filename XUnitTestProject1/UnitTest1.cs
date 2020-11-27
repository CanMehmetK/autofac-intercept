
using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace XUnitTestProject1
{
    
    public class UnitTest1
    {
        [Fact]
        public async System.Threading.Tasks.Task Test1Async()
        {
            var webHostBuilder = new Mock<IWebHostBuilder>();
          
        }
    }

    public interface ITip { int Get { get; } }
    public class A:ITip
    {
        public int Get { get { return 1; } }
    }
    public class B : ITip
    {
        public int Get { get { return 2; } }
    }
}
