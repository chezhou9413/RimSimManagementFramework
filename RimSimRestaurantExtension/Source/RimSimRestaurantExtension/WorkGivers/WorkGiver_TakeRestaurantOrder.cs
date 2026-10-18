namespace RimSimRestaurantExtension.WorkGivers
{
    //派发较低优先级的桌边接待，职责是让服务员先送完可配送餐品再接新单。
    public class WorkGiver_TakeRestaurantOrder : WorkGiver_DeliverRestaurantOrder
    {
        protected override bool Reception => true;
    }
}
