// See https://aka.ms/new-console-template for more information
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.ExceptionServices;

Console.Write("Hello, ");

MyTask.iterate(PrintAsync()).Wait();

static IEnumerable<MyTask> PrintAsync()
{
    for(int i=0;i<1000; i++)
    {
        yield return MyTask.Delay(1000);
        Console.WriteLine(i);
    }
}


//MyTask.Delay(2000).ContinueWith(delegate
//{
//    Console.Write("World");
//    return MyTask.Delay(2000).ContinueWith(delegate 
//    {
//        Console.Write("Balaji");
//        //return MyTask.Delay(2000).ContinueWith(delegate
//        //{
//        //    Console.Write("Balaji");
//        //});
//    });
//}).Wait();


//AsyncLocal<int> asyncLocal=new();
//List<MyTask> tasks=new List<MyTask>();
//for(int i=0; i<100;i++)
//{
//    asyncLocal.Value=i;
//    tasks.Add(MyTask.Run(()=>
//    {
//        Console.WriteLine($"Value of AsyncLocal is {asyncLocal.Value} and Thread Id is {Thread.CurrentThread.ManagedThreadId}");
//Thread.Sleep(1000);
//    }));

//}
//MyTask.WhenAll(tasks).Wait();
//foreach (MyTask task in tasks)
//{

//    task.Wait();
//    Console.WriteLine($"Thread id is {Thread.CurrentThread.ManagedThreadId}");
//}
Console.WriteLine("All Done");
Console.ReadLine();
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

    public static MyTask WhenAll(List<MyTask> tasks)
    {
        MyTask task = new();
        if(tasks.Count==0)
        {
            task.SetResult();
        }

        int taskcount= tasks.Count;
        Action Continuation = () => {

            if(Interlocked.Decrement(ref taskcount)==0)
            {
                task.SetResult();

            }
        };

        foreach(var t in tasks)
        {
            t._continuation = Continuation;
        }
        return task;

    }

    public void Wait()
    {
       ManualResetEventSlim? manualResetEventSlim =null;
       lock(this)
       {
          //  Console.WriteLine($"Thread Id is {Thread.CurrentThread.ManagedThreadId} and Completed {_completed}");
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

    public static MyTask Delay(int Interval)
    {
        MyTask myTask = new();
        new Timer(_ => myTask.SetResult()).Change(Interval, -1);
        return myTask;
    }

    public MyTask ContinueWith(Action action)
    {
        MyTask t = new();

        Action callback = () =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                t.SetException(e);
                return;
            }
            t.SetResult();
        };

        lock (this)
        {
            if (_completed)
            {
                MyThreadPool.QueueUserWorkItem(callback);
            }
            else
            {
                _continuation = callback;
                _context = ExecutionContext.Capture();
            }
        }

        return t;
    }

    public MyTask ContinueWith(Func<MyTask> action)
    {
        MyTask t=new();
       Action callback=() =>
       {
           try
           {
               MyTask next = action();
               next.ContinueWith(delegate
               {
                   if (next._exception is not null) { t.SetException(next._exception); }
                   else { t.SetResult(); }
               });
               // Console.WriteLine($"Thread id inside continue is {Thread.CurrentThread.ManagedThreadId}");
           }
           catch (Exception ex)
           {
               t.SetException(ex);
               return;
           }
      //  t.SetResult();
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

    public static MyTask iterate(IEnumerable<MyTask> tasks)
    {
        MyTask t=new MyTask();
        IEnumerator<MyTask> e=tasks.GetEnumerator();
        void MoveNext()
        {
            try
            {
                while(e.MoveNext())
                {
                    MyTask next=e.Current;
                    if (next._isCompleted)
                    {
                        next.Wait();
                        continue;
                    }
                    next.ContinueWith(MoveNext);
                    return;
                }
            }
            catch(Exception ex) {

                t.SetException(ex); return;
            }
        }
        MoveNext();
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