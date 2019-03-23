Jones Onie - the FX Workhorse
Machine Learning on higher dimensional time series data.

The following preparatory steps are necessary to construct our model:
1.  Collect time series data from your data provider of choice, I chose Oanda as my provider for foreign exchange information.
2.  Store time series data in csv files for easy reference.  I collected data at 2 minute intervals for one month periods, between January 2005 and December 2018.
3.  Compute statistical analysis metrics such as average, standard deviation and correlation for the time series.
	a. The time intervals in steps 3 and 4 are subject to hyperparameter tuning.
	b. These parameters may be found in the ReferenceModule.
4.  Once the statistical information has been collected, it is now possible to compute a vector for a set of input data.
	a.  To do this, we collect a data sample of size similar size to our historical data sample size, and project training future data onto our observation by means a of correlated similarity-ordered, time of day and week factored, logistic regression model. 
	b.  We then test this regression and take note of accuracy with respect to time of day and week.  

The following folder structure is utilized for storing informtion:
- C:\JonesOnie\CSV\MinuteData
- C:\JonesOnie\CSV\Statistics\Averages\Backward
- C:\JonesOnie\CSV\Statistics\Averages\Forward
- C:\JonesOnie\CSV\Statistics\Deviations\Backward
- C:\JonesOnie\CSV\Statistics\Deviations\Forward
- C:\JonesOnie\CSV\Statistics\Correlations\Instruments\Backward
- C:\JonesOnie\CSV\Statistics\Correlations\Instruments\Forward
- C:\JonesOnie\CSV\Optimizations
- C:\JonesOnie\CSV\Projections
	
Trading Algorithm Design:
- Taking into account the accuracy of the model at particular times of day and the week, for each instrument, we may use this figure in conjunction with the projected 
