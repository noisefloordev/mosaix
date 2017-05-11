(function()
{
    function closest(e, selector)
    {
        while(e)
        {
            if(e.matches(selector))
                return e;
            e = e.parentElement;
        }
        return null;
    }
/*
    var selected_tab = null;
    function select_tab(tab)
    {
        if(selected_tab != null)
        {
            document.querySelector("[data-tab=" + selected_tab + "]").hidden = true;
            delete document.querySelector("[data-tab-button=" + selected_tab + "]").dataset.active;
        }
        selected_tab = tab;
        document.querySelector("[data-tab=" + selected_tab + "]").hidden = false;
        document.querySelector("[data-tab-button=" + selected_tab + "]").dataset.active = "selected";
    };

    window.addEventListener("load", function() {
        select_tab("intro");
        document.querySelector(".header").addEventListener("click", function(e) {
            var button = closest(e.target, "[data-tab-button]");
            if(button == null)
                return;

            var tab = button.dataset.tabButton;
            select_tab(tab);
        });
    });
*/
})();

