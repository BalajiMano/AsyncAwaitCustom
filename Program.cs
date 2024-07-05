// See https://aka.ms/new-console-template for more information
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

Console.WriteLine("Hello, World!");


AsyncLocal<int> asyncLocal=new();
List<MyTask> tasks=new List<MyTask>();
for(int i=0; i<100;i++)
{
    asyncLocal.Value=i;
    tasks.Add(MyTask.Run(()=>
    {
        Console.WriteLine($"Value of AsyncLocal is {asyncLocal.Value} and Thread Id is {Thread.CurrentThread.ManagedThreadId}");
Thread.Sleep(1000);
    }));

}
foreach (MyTask task in tasks)
{
  
    task.Wait();
    Console.WriteLine($"Thread id is {Thread.CurrentThread.ManagedThreadId}");
}
Console.WriteLine("All Task Done");
class MyTask
{

    private bool _completed;
    private Exception? _exception;
    private Action? _continuation;
   private ExecutionContext? _context;
    public bool _isCompleted
    {
        get 
        {
            lock(this)
            {
            return _completed;
            }
        }
    }
    public void SetResult()=> Complete(null);
    public void SetException(Exception ex)=>Complete(ex);
    private void Complete(Exception? exception)
    {
        lock (this) 
        {
        if(_completed) throw new InvalidOperationException("Stop messing up my code");
        _completed=true;
        _exception=exception;

        if(_continuation is not null)
        {
            MyThreadPool.QueueUserWorkItem(delegate 
            {
                if(_context is null)
                {
                    _continuation();
                }
                else
                {
                     ExecutionContext.Run(_context,(object? state)=> ((Action) state!).Invoke(),_continuation);
                }
            });
        }
        }

    }

    public void Wait()
    {
       ManualResetEventSlim? manualResetEventSlim =null;
       lock(this)
       {
            Console.WriteLine($"Thread Id is {Thread.CurrentThread.ManagedThreadId} and Completed {_completed}");
        if(!_completed) 
        {
            manualResetEventSlim=new();
            ContinueWith(manualResetEventSlim.Set);
        }

       }
       manualResetEventSlim?.Wait();

        if (_exception is not null)
        {
            ExceptionDispatchInfo.Throw(_exception);
        }
    }

    public MyTask ContinueWith(Action action)
    {
        MyTask t=new();
       Action callback=() =>
       {
        try
        {
            action();
               Console.WriteLine($"Thread id inside continue is {Thread.CurrentThread.ManagedThreadId}");
           }
        catch (Exception ex)
        {
            t.SetException(ex);
        }
        t.SetResult();
       };

       lock(this)
       {
        if(_completed)
        {
            MyThreadPool.QueueUserWorkItem(callback);
        }
        else
        {
            _continuation=callback;
            _context=ExecutionContext.Capture();
        }
       }
       return t;
    }

public static MyTask  Run(Action action)
{
    MyTask myTask=new();
        try
        {
            MyThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    myTask.SetException(ex);
                    return;
                }
                myTask.SetResult();
            });
        }
        catch(Exception ex) { Console.WriteLine(ex); }
    return myTask;
}


}

static class MyThreadPool 
{
    private static readonly BlockingCollection<(Action,ExecutionContext?)> s_workingItem = new();
    public static void QueueUserWorkItem(Action action)
    {
        s_workingItem.Add((action, ExecutionContext.Capture()));
    }
    static MyThreadPool()
    {


    
    for (int i = 0;i<Environment.ProcessorCount ; i++) 
    {
        new Thread(() =>
        {
            while(true)
            {
                (Action workItem,ExecutionContext? context)= s_workingItem.Take();
                if (context is null)
                {
                    workItem();

                }
                else
                {
                    ExecutionContext.Run(context,(object? state)=> ((Action) state!).Invoke(),workItem);
                }
            }

        }){IsBackground=true}.Start();

    }   


    }

}